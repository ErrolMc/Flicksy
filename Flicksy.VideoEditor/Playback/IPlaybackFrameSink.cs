namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// The handle the <see cref="PlaybackEngine"/> uses to point the preview at the decode-ahead pump
/// during playback and back to synchronous decode when stopped. Implemented by
/// <c>PreviewViewModel</c>; set to the pump on <c>Play</c> and to <c>null</c> on <c>Pause</c>.
/// Set-only — the engine never reads it back. Clearing it to null refreshes the preview immediately
/// (the implementation re-renders via the scrub path), so a seek-while-playing repaints the seeked
/// frame at once instead of leaving the stale pumped frame on screen until the next Play. Setting it to
/// the pump does not render (that would consume Play's prime frame — see the implementation remarks).
/// </summary>
public interface IPlaybackFrameSink
{
    IPlaybackFrameSource? PlaybackFrames { set; }

    /// <summary>
    /// The decode scale the preview is currently rendering at (target pixel width ÷ project width).
    /// The engine reads it to warm the paused-state prefetch at the same scale the next <c>Play</c>
    /// will use, so the prebuffered frames are reusable rather than the wrong size.
    /// </summary>
    double CurrentDecodeScale { get; }
}
