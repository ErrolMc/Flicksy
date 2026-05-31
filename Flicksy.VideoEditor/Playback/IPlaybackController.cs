namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// The transport-facing surface of the playback engine. <see cref="ViewModels.TransportViewModel"/>
/// delegates its play/pause and frame-step commands here; the engine writes <c>Playhead</c> /
/// <c>IsPlaying</c> back on the transport (which the preview, timeline and ruler already
/// observe). Kept as an interface so the transport doesn't take a hard dependency on the
/// concrete engine and can fall back to no-op stepping when no engine is attached (tests).
/// </summary>
public interface IPlaybackController
{
    /// <summary>Start playing if paused, pause if playing.</summary>
    void TogglePlayPause();

    /// <summary>Pause and move the playhead by <paramref name="delta"/> frames (clamped to the timeline).</summary>
    void StepFrame(int delta);
}
