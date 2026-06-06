using System;
using System.Collections.Generic;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Composition;

/// <summary>
/// Lazily-opened, <c>Clip.Id</c>-keyed cache of <see cref="IMediaDecoder"/>s. Keyed by clip
/// (not source) because two clips of the same source at different source-times each need an
/// independent decode cursor (ADR 0004).
/// <para>
/// One instance backs each render seam — <see cref="SkiaCompositor"/> (video, UI thread) and
/// <see cref="AudioMixer"/> (audio, NAudio thread) hold separate caches (ADR 0005). That
/// gives a <c>Streams=Both</c> clip two decoders, one per stream, each on its own thread —
/// FFmpeg seeks the container rather than a single stream, so sharing one <c>MediaFile</c>
/// across the video and audio cursors would thrash. A cache instance is therefore single-
/// threaded by construction; it does no internal locking.
/// </para>
/// </summary>
public sealed class MediaDecoderCache : IDisposable
{
    private readonly Dictionary<Guid, (IMediaDecoder Decoder, double Scale)> _decoders = new();
    private bool _disposed;

    /// <summary>
    /// Return the decoder for <paramref name="clip"/>, opening it on first reference. Returns
    /// null when the clip has no resolvable source path or the open/probe fails — the caller
    /// renders nothing for the clip (which already reds-out in the timeline).
    /// <para>
    /// <paramref name="decodeScale"/> &lt; 1 opens the decoder at a reduced <c>TargetVideoSize</c>
    /// for on-the-fly preview downscale (ADR 0008); the decoder is re-opened when the scale
    /// changes (e.g. the preview-quality dropdown), otherwise reused. Decoders are otherwise never
    /// evicted during the cache's lifetime; all are disposed in <see cref="Dispose"/>.
    /// </para>
    /// </summary>
    public IMediaDecoder? GetOrCreate(MediaClip clip, int targetSampleRate, double decodeScale = 1.0)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MediaDecoderCache));

        if (_decoders.TryGetValue(clip.Id, out var existing))
        {
            // Reuse the open cursor unless the preview decode scale changed, in which case the
            // decoder must re-open to emit frames at the new size.
            if (existing.Scale == decodeScale) return existing.Decoder;
            try { existing.Decoder.Dispose(); } catch { /* best-effort */ }
            _decoders.Remove(clip.Id);
        }

        var source = clip.Source;
        var path = source?.SourcePath;
        if (string.IsNullOrEmpty(path)) return null;

        try
        {
            var decoder = new FFmpegMediaDecoder(path, targetSampleRate, ResolveTargetSize(source!, decodeScale));
            _decoders[clip.Id] = (decoder, decodeScale);
            return decoder;
        }
        catch
        {
            // Probe failure — render nothing for this clip. Silent for now, matching the
            // rest of the pipeline; a diagnostics hook lands with the logging work.
            return null;
        }
    }

    // Below project resolution, decode proxy-sized frames (swscale rescales post-decode). At
    // scale >= 1 or unknown source dims, decode native (null = no rescale).
    private static System.Drawing.Size? ResolveTargetSize(MediaSource source, double decodeScale)
    {
        if (decodeScale >= 1.0 || source.Width <= 0 || source.Height <= 0) return null;
        int w = Math.Max(2, (int)Math.Round(source.Width * decodeScale));
        int h = Math.Max(2, (int)Math.Round(source.Height * decodeScale));
        return new System.Drawing.Size(w, h);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var entry in _decoders.Values)
        {
            try { entry.Decoder.Dispose(); } catch { /* swallow — best-effort cleanup */ }
        }
        _decoders.Clear();
    }
}
