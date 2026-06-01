namespace Flicksy.VideoEditor.Project;

/// <summary>
/// Which streams of a <see cref="MediaClip"/>'s referenced <see cref="MediaSource"/> the
/// compositor should render. Defaults to <see cref="Both"/> for a video+audio source dropped
/// on a video track; the per-clip Detach audio command flips a clip to
/// <see cref="Video"/> and adds a paired <see cref="Audio"/> clip on a new audio track.
/// Track-kind compatibility is enforced by the drop matrix and the split operation, not by
/// the model.
/// </summary>
public enum ClipStreams
{
    Both,
    Video,
    Audio,
}
