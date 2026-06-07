using System;

namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// The decode primitive <see cref="VideoPrefetchPump"/> drives: produce one frame's bundle and
/// recycle a spent one. Factored out so the pump's queue / seek / generation / lifetime machinery
/// is unit-testable against a fake source — no FFmpeg, Skia, or WPF. The production implementation
/// (<see cref="ProjectBundleSource"/>) plans the frame and decodes its video layers; a test fake
/// returns deterministic sentinel buffers and tracks rent/return for leak assertions.
/// </summary>
public interface IFrameBundleSource : IDisposable
{
    /// <summary>
    /// Plan + decode all video layers for <paramref name="frame"/> at <paramref name="decodeScale"/>,
    /// stamping the result with <paramref name="generation"/>. Returns <c>null</c> to <b>skip</b> the
    /// frame (e.g. a torn read because the timeline was edited mid-playback) — the pump advances past
    /// it and the consumer holds the previous frame. Must not throw; a skip is signalled by null.
    /// Called on the pump's producer thread, never under the pump's lock.
    /// </summary>
    FrameBundle? Produce(int frame, int generation, double decodeScale);

    /// <summary>Return every rented buffer in <paramref name="bundle"/>. Idempotent.</summary>
    void Recycle(FrameBundle bundle);

    /// <summary>
    /// Total timeline frames; the pump produces only for <c>[0, TotalFrames)</c>. Re-read each
    /// producer cycle so growth (a clip added mid-play) is picked up.
    /// </summary>
    int TotalFrames { get; }
}
