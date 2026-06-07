using System.Collections.Generic;
using Flicksy.VideoEditor.Composition;

namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// The consumer (UI-thread) face of the decode-ahead pump that <c>PreviewViewModel</c> drives
/// during playback. Extends <see cref="IClipFrameProvider"/> (so it can be handed straight to
/// <c>ICompositor.RenderFrame</c>) with the per-frame consume protocol:
/// <list type="number">
///   <item><see cref="BeginFrame"/> claims the pre-decoded bundle for a frame (or reports a miss).</item>
///   <item>The compositor renders, pulling each layer's frame via <see cref="IClipFrameProvider.Acquire"/>
///   and the layer list via <see cref="CurrentLayers"/>.</item>
///   <item><see cref="EndFrame"/> releases the bundle back to the pump.</item>
/// </list>
/// All three are called only on the UI thread, strictly paired, single-frame-in-flight.
/// </summary>
public interface IPlaybackFrameSource : IClipFrameProvider
{
    /// <summary>
    /// Claim the newest ready frame at or before <paramref name="frame"/> at
    /// <paramref name="decodeScale"/> (intermediate frames are dropped to stay live). Returns true
    /// when one is available (the caller must then render and call <see cref="EndFrame"/>); false on
    /// a miss — nothing decoded up to the playhead yet, or the quality changed (the scale is captured
    /// and the queue re-primed) — in which case the caller skips rendering and keeps the previous
    /// frame on screen, and must NOT call <see cref="EndFrame"/>.
    /// </summary>
    bool BeginFrame(int frame, double decodeScale);

    /// <summary>Release the bundle claimed by a successful <see cref="BeginFrame"/>. Idempotent.</summary>
    void EndFrame();

    /// <summary>The planner snapshot for the claimed frame — pass as <c>plannedLayers</c> to the compositor.</summary>
    IReadOnlyList<CompositionLayer> CurrentLayers { get; }
}
