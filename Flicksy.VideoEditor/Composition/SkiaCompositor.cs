using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flicksy.Drawing.Media;
using Flicksy.Drawing.Source;
using Flicksy.VideoEditor.Project;
using SkiaSharp;

namespace Flicksy.VideoEditor.Composition;

/// <summary>
/// <see cref="ICompositor"/> backed by SkiaSharp's CPU surface. Wraps each call's target
/// <see cref="WriteableBitmap"/>'s back buffer via <see cref="SKBitmap.InstallPixels(SKImageInfo, IntPtr, int)"/>
/// so paints land directly in WPF-bindable memory — no intermediate Skia surface, no
/// extra copy. <see cref="CompositionPlanner.PlanFrame"/> supplies the layer list (or the caller
/// passes a pre-planned snapshot); this class owns paint dispatch and pulls each video layer's
/// frame through an <see cref="IClipFrameProvider"/> — a synchronous, self-owned
/// <see cref="DecodingFrameProvider"/> by default. Audio mixing moved to <see cref="AudioMixer"/>
/// per ADR 0005.
/// <para>
/// Render scale: the target bitmap's size is the physical surface; the project resolution is
/// the logical space layers position in. A target smaller than the project (proxy /
/// preview-quality mode — ADR 0008) is painted via a single canvas pre-scale, so every
/// per-layer transform/crop stays in project pixels. Export passes a full-resolution target
/// (scale 1). The scale is derived from the target size, so <see cref="RenderFrame"/> needs
/// no extra parameter.
/// </para>
/// <para>
/// Threading: per <see cref="ICompositor"/>, calls are single-call-in-flight on one
/// thread at a time; the class is not thread-safe across concurrent callers.
/// <see cref="RenderFrame"/> paints into the caller's unfrozen
/// <see cref="WriteableBitmap"/>, so the compositor and whoever presents that bitmap
/// must share a thread — the UI thread today. Two independent constraints already point
/// the same way: the <see cref="GraphicsClip"/> path needs a Dispatcher for
/// <see cref="RenderTargetBitmap"/>, and an unfrozen bitmap can't cross threads.
/// During playback the per-frame <em>decode</em> runs ahead on a background thread and is supplied
/// via the optional <see cref="IClipFrameProvider"/> argument; compositing itself stays on the
/// shared (UI) thread, so both constraints above still hold (ADR 0009).
/// </para>
/// </summary>
public sealed class SkiaCompositor : ICompositor
{
    // The synchronous decode path (export / scrub / static preview). Lazily created on first use
    // because the compositor doesn't know the project (hence the sample rate) until RenderFrame.
    // Skipped entirely when a caller supplies its own frame provider (the playback pump).
    private DecodingFrameProvider? _defaultFrames;
    private bool _disposed;

    public void RenderFrame(
        Project.Project project,
        int frame,
        WriteableBitmap target,
        IClipFrameProvider? frames = null,
        IReadOnlyList<CompositionLayer>? plannedLayers = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(target);
        if (_disposed) 
            throw new ObjectDisposedException(nameof(SkiaCompositor));

        int width = project.Settings.ResolutionWidth;
        int height = project.Settings.ResolutionHeight;
        int sampleRate = project.Settings.AudioSampleRate;

        // The caller owns the bitmap and reuses it across frames (no per-frame allocation).
        // Its size is the *physical* render target; the project resolution is the *logical*
        // space every layer is positioned in. A target smaller than the project (proxy /
        // preview-quality mode — ADR 0008) paints the same project-space layer stack scaled
        // down to fit; export passes a full-resolution bitmap and gets scale 1. InstallPixels
        // maps the SKImageInfo straight over the back buffer, so the info and the dirty rect
        // must use the target's actual dimensions, not the project's.
        int targetWidth = target.PixelWidth;
        int targetHeight = target.PixelHeight;
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentException(
                $"Target bitmap has non-positive size {targetWidth}x{targetHeight}.",
                nameof(target));
        }

        // Decode source: the caller's prefetch provider during playback, else the lazily-created
        // synchronous one (decodes on this thread — the canonical export/scrub/static path).
        IClipFrameProvider activeFrames = frames ?? (_defaultFrames ??= new DecodingFrameProvider(sampleRate, preferHardwareVideo: true));

        target.Lock();
        try
        {
            // Pbgra32 + SKAlphaType.Premul: WPF's only fully blendable format pair.
            var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var skBitmap = new SKBitmap();
            skBitmap.InstallPixels(info, target.BackBuffer, target.BackBufferStride);
            using var canvas = new SKCanvas(skBitmap);

            canvas.Clear(SKColors.Black);

            // Proxy pre-scale: map the full project-resolution coordinate space onto the
            // (possibly smaller) target. Layers keep reasoning in project pixels; this single
            // scale shrinks the whole stack. At full resolution sx = sy = 1 (identity). Each
            // layer Save()s, Concat()s its source->project matrix onto this base, then
            // Restore()s, so the base scale persists across the layer loop.
            canvas.Scale((float)targetWidth / width, (float)targetHeight / height);

            // Decode scale = render scale: at reduced preview quality the decoders also emit
            // smaller frames (ADR 0008), so convert/copy/raster shrink too, not just present.
            double decodeScale = (double)targetWidth / width;

            // Use the snapshot the prefetch worker already planned (so we never re-walk a project
            // the UI thread may be mutating mid-playback); plan ourselves on the synchronous path.
            IReadOnlyList<CompositionLayer> layers = plannedLayers ?? CompositionPlanner.PlanFrame(project, frame);
            foreach (CompositionLayer layer in layers)
            {
                // Audio-only layers don't contribute to the visual frame.
                if (layer.Track.Kind == TrackKind.Audio) 
                    continue;

                PaintLayer(canvas, activeFrames, layer, width, height, decodeScale);
            }
        }
        finally
        {
            // AddDirtyRect + Unlock raises WriteableBitmap's own invalidation, so the bound
            // Image repaints in place — the caller need not reassign its source each frame.
            // The bitmap stays unfrozen (it's reused), so presentation must be same-thread
            // per the ADR 0004 contract.
            target.AddDirtyRect(new Int32Rect(0, 0, targetWidth, targetHeight));
            target.Unlock();
        }
    }

    public void Dispose()
    {
        if (_disposed) 
            return;

        _disposed = true;
        _defaultFrames?.Dispose();
    }

    // ---- Paint dispatch -----------------------------------------------------

    private void PaintLayer(SKCanvas canvas, IClipFrameProvider frames, CompositionLayer layer, int projectWidth, int projectHeight, double decodeScale)
    {
        switch (layer.Clip)
        {
            case MediaClip mediaClip when mediaClip.Streams != ClipStreams.Audio:
                PaintMediaClip(canvas, frames, layer, mediaClip, projectWidth, projectHeight, decodeScale);
                break;
            case GraphicsClip graphicsClip:
                PaintGraphicsClip(canvas, graphicsClip, projectWidth, projectHeight);
                break;
            // Audio-only MediaClips and unknown subtypes: no visual output.
        }
    }

    private void PaintMediaClip(SKCanvas canvas, IClipFrameProvider frames, CompositionLayer layer, MediaClip clip, int projectWidth, int projectHeight, double decodeScale)
    {
        if (clip.IsBroken) 
            return;

        VideoFrame? maybeFrame = frames.Acquire(clip, layer.SourceTime, decodeScale);
        if (maybeFrame is null) 
            return;

        VideoFrame videoFrame = maybeFrame.Value;

        // The transform and crop reason in NATIVE source pixels (clip.Source.Width/Height); the
        // decoded frame may be smaller under preview downscale (ADR 0008). Fall back to the frame's
        // own size when native dims are unknown.
        MediaSource? source = clip.Source;
        int nativeWidth = source is { Width: > 0 } ? source.Width : videoFrame.Width;
        int nativeHeight = source is { Height: > 0 } ? source.Height : videoFrame.Height;

        try
        {
            // Pin the rented byte[] so Skia can read it directly. SKImage.FromPixels does
            // not copy — the memory must stay valid for the lifetime of the image.
            GCHandle handle = GCHandle.Alloc(videoFrame.Buffer, GCHandleType.Pinned);
            try
            {
                var srcInfo = new SKImageInfo(videoFrame.Width, videoFrame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using SKImage image = SKImage.FromPixels(srcInfo, handle.AddrOfPinnedObject(), videoFrame.Stride);

                // Matrix maps the NATIVE source extent -> project, so transforms/crops stay
                // resolution-independent regardless of the decoded frame's size.
                (SKMatrix matrix, SKRect? srcRect) = BuildLayerMatrix(clip.Transform, nativeWidth, nativeHeight, projectWidth, projectHeight);

                canvas.Save();
                // Concat (not SetMatrix) so the global preview-quality pre-scale on the canvas
                // composes with this layer's source->project matrix: total = qualityScale * matrix.
                canvas.Concat(matrix);
                if (srcRect is { } crop)
                {
                    canvas.ClipRect(crop);
                }
                // Map the (possibly smaller) decoded frame onto the native source extent so a
                // downscaled preview frame composites identically, just at lower fidelity.
                // Identity when frame == native (Full quality).
                if (videoFrame.Width != nativeWidth || videoFrame.Height != nativeHeight)
                {
                    canvas.Concat(SKMatrix.CreateScale(
                        (float)nativeWidth / videoFrame.Width,
                        (float)nativeHeight / videoFrame.Height));
                }
                canvas.DrawImage(image, 0, 0);
                canvas.Restore();
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            // Hand the frame back to the provider (ArrayPool return on the sync path; recycle
            // into the bundle on the prefetch path).
            frames.Release(videoFrame);
        }
    }

    private void PaintGraphicsClip(SKCanvas canvas, GraphicsClip clip, int projectWidth, int projectHeight)
    {
        if (clip.Items.Count == 0) 
            return;

        // GraphicsClip items render through WPF's DrawingContext. We bounce through a
        // project-resolution RenderTargetBitmap, copy the pixels out, and hand them to
        // Skia. This is allocation-heavy (one full RTB + one byte[] per graphics layer
        // per frame); a future Skia-native render path would eliminate it. The bigger
        // structural constraint: RenderTargetBitmap.Render needs a Dispatcher, so this
        // path only works on the UI thread. The preview wiring (step 7) does call from
        // the UI thread; off-thread playback (step 11) may need to address this.
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            foreach (DrawingItem item in clip.Items)
            {
                item.Render(dc);
            }
        }

        var rtb = new RenderTargetBitmap(projectWidth, projectHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        int stride = projectWidth * 4;
        byte[] pixels = new byte[stride * projectHeight];
        rtb.CopyPixels(pixels, stride, 0);

        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var srcInfo = new SKImageInfo(projectWidth, projectHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using SKImage image = SKImage.FromPixels(srcInfo, handle.AddrOfPinnedObject(), stride);

            // Graphics clips draw in project-resolution space already, so the layer
            // matrix maps from (projectWidth × projectHeight) source to the same project
            // frame. Default Transform2D yields the identity matrix.
            (SKMatrix matrix, _) = BuildLayerMatrix(clip.Transform, projectWidth, projectHeight, projectWidth, projectHeight);

            canvas.Save();
            // Concat (not SetMatrix) so the canvas's global proxy pre-scale is preserved.
            canvas.Concat(matrix);
            canvas.DrawImage(image, 0, 0);
            canvas.Restore();
        }
        finally
        {
            handle.Free();
        }
    }

    // ---- Matrix + helpers ---------------------------------------------------

    /// <summary>
    /// Build the source→project matrix for one layer. Transform2D semantics:
    /// <list type="bullet">
    ///   <item><c>Position</c> = clip-center offset from project-frame center, in project pixels.</item>
    ///   <item><c>Scale</c> = per-axis scaling of source pixels (1,1 = pixel-for-pixel).</item>
    ///   <item><c>RotationDegrees</c> = clockwise rotation around the clip's center.</item>
    ///   <item><c>CropRect</c> (optional) = source-space rect of the visible region. When set,
    ///         the clip's center becomes the crop's center and the painter clips drawing
    ///         to the crop rect in source space.</item>
    /// </list>
    /// Composition: <c>M = T_clipCenter * R * S * T_-sourceCenter</c>.
    /// </summary>
    private static (SKMatrix Matrix, SKRect? SrcClipRect) BuildLayerMatrix(
        Transform2D transform, int sourceWidth, int sourceHeight, int projectWidth, int projectHeight)
    {
        float sourceCenterX, sourceCenterY;
        SKRect? srcClip = null;

        if (transform.CropRect is { } crop)
        {
            sourceCenterX = (float)(crop.X + crop.Width * 0.5);
            sourceCenterY = (float)(crop.Y + crop.Height * 0.5);
            srcClip = new SKRect(
                (float)crop.X,
                (float)crop.Y,
                (float)(crop.X + crop.Width),
                (float)(crop.Y + crop.Height));
        }
        else
        {
            sourceCenterX = sourceWidth * 0.5f;
            sourceCenterY = sourceHeight * 0.5f;
        }

        float clipCenterX = projectWidth * 0.5f + (float)transform.Position.X;
        float clipCenterY = projectHeight * 0.5f + (float)transform.Position.Y;

        // M = T_clipCenter * R * S * T_-sourceCenter, computed via SKMatrix.Concat which
        // returns first * second. Read bottom-up: T_-sourceCenter applies first, T_clipCenter last.
        SKMatrix m = SKMatrix.CreateTranslation(-sourceCenterX, -sourceCenterY);
        m = SKMatrix.Concat(SKMatrix.CreateScale((float)transform.Scale.X, (float)transform.Scale.Y), m);
        m = SKMatrix.Concat(SKMatrix.CreateRotationDegrees((float)transform.RotationDegrees), m);
        m = SKMatrix.Concat(SKMatrix.CreateTranslation(clipCenterX, clipCenterY), m);

        return (m, srcClip);
    }
}
