using System;
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
///         UI thread today). Cross-thread / decode-ahead playback is #11's deferred phase 2.</item>
///   <item>Decoder ownership is internal — the compositor maintains its own
///         <c>Clip.Id</c>-keyed cache of <c>IMediaDecoder</c>s. Callers see only the
///         pixels written into their bitmap.</item>
/// </list>
/// </summary>
public interface ICompositor : IDisposable
{
    /// <summary>
    /// Paint one composited frame into <paramref name="target"/>, a caller-owned
    /// <see cref="WriteableBitmap"/> whose dimensions must equal the project resolution
    /// (<c>ProjectSettings.{ResolutionWidth, ResolutionHeight}</c>). The compositor
    /// <c>Lock</c>s the bitmap, blits the layer stack into its back buffer, marks it dirty,
    /// and <c>Unlock</c>s — the bound <c>Image</c> repaints in place, so the caller need
    /// not reassign its <c>Image.Source</c> between frames. Throws
    /// <see cref="System.ArgumentException"/> when <paramref name="target"/>'s size differs
    /// from the project resolution.
    /// </summary>
    void RenderFrame(Project.Project project, int frame, WriteableBitmap target);
}
