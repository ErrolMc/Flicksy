using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace Flicksy.VideoEditor.Composition;

/// <summary>
/// Produces a composited video frame for a <c>(Project, frame)</c> input. Audio mixing is a
/// separate seam — <see cref="IAudioMixer"/> — split out by ADR 0005 so video (UI thread) and
/// audio (NAudio thread) run independently; each caller composes what it needs.
/// <para>
/// Contract (per ADR 0004):
/// </para>
/// <list type="bullet">
///   <item>Synchronous and single-call-in-flight. The compositor does no internal
///         locking and must not be invoked concurrently from multiple threads.</item>
///   <item>Caller-owned output. <see cref="RenderFrame"/> paints into a
///         <see cref="WriteableBitmap"/> the caller supplies and reuses across frames, so
///         the compositor allocates no per-frame frame buffer. The bitmap is left
///         unfrozen, so the compositor and whoever presents it must share one thread (the
///         UI thread today). Compositing always runs on that shared thread; during playback the
///         per-frame <em>decode</em> is supplied off-thread via the optional frame provider (ADR 0009).</item>
///   <item>Decode source is pluggable — by default the compositor decodes synchronously through
///         its own <c>Clip.Id</c>-keyed cache; a caller may instead pass an
///         <see cref="IClipFrameProvider"/> (the playback pump). Callers see only the pixels
///         written into their bitmap.</item>
/// </list>
/// </summary>
public interface ICompositor : IDisposable
{
    /// <summary>
    /// Paint one composited frame into <paramref name="target"/>, a caller-owned
    /// <see cref="WriteableBitmap"/>. The target's dimensions define the render scale: the
    /// layer stack is positioned in project-resolution coordinates
    /// (<c>ProjectSettings.{ResolutionWidth, ResolutionHeight}</c>) and scaled to fit the
    /// target. A full-resolution bitmap renders 1:1 (export / canonical path); a smaller one
    /// renders a proxy, lower-quality preview (ADR 0008) — every per-clip transform/crop still
    /// reasons in project pixels, so only fidelity differs. The compositor <c>Lock</c>s the
    /// bitmap, blits into its back buffer, marks it dirty, and <c>Unlock</c>s — the bound
    /// <c>Image</c> repaints in place, so the caller need not reassign its <c>Image.Source</c>
    /// between frames. Throws <see cref="System.ArgumentException"/> only when
    /// <paramref name="target"/> has a non-positive dimension.
    /// <para>
    /// <paramref name="frames"/> is the source of each video layer's decoded frame. When
    /// <c>null</c> (export / scrub / static preview) the compositor decodes synchronously on the
    /// calling thread via its own cache — the canonical path. During playback the off-thread
    /// decode-ahead worker passes itself here so the codec never runs on the UI thread (ADR 0009);
    /// it also passes <paramref name="plannedLayers"/> — the layer list it already planned — so the
    /// compositor renders that immutable snapshot instead of re-walking the live (possibly mutating)
    /// project. When both are <c>null</c> the compositor plans the frame itself.
    /// </para>
    /// </summary>
    void RenderFrame(
        Project.Project project,
        int frame,
        WriteableBitmap target,
        IClipFrameProvider? frames = null,
        IReadOnlyList<CompositionLayer>? plannedLayers = null);
}
