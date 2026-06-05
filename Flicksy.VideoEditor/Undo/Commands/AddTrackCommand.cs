using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Adds one <see cref="Track"/> to the project's track list — currently the audio track spun up by
/// <see cref="TimelineViewModel.DetachAudio"/>. Deliberately granular: detach bundles this with a
/// separate <see cref="AddClipCommand"/> (the audio clip) and a <see cref="ChangeClipStreamsCommand"/>
/// (the source clip's stream flip) inside one <c>CompositeCommand</c>, so a future "detach onto an
/// existing audio track" can drop just this command and reuse the other two.
/// <para>
/// The track is already in <see cref="Project.Project.Tracks"/> when this is pushed (the edit mutated
/// live), matching the "push after mutation" convention — <see cref="Redo"/> runs only when stepping
/// forward through the redo stack. The insertion index is captured so redo restores the track at its
/// original position rather than re-appending. Selection is untouched (a track carries no per-track
/// selection; the bundling <c>CompositeCommand</c>'s <c>TimelineSelectionScope</c> owns clip
/// selection across the whole step). Assumes the track is empty on undo — in the detach bundle the
/// child order removes the audio clip first.
/// </para>
/// </summary>
public sealed class AddTrackCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly Track _track;
    private readonly int _index;

    public AddTrackCommand(TimelineViewModel viewModel, Track track, int index)
    {
        _viewModel = viewModel;
        _track = track;
        _index = index;
    }

    public void Redo()
    {
        var tracks = _viewModel.Project.Tracks;
        var index = _index < 0 || _index > tracks.Count ? tracks.Count : _index;
        tracks.Insert(index, _track);
    }

    public void Undo()
    {
        _viewModel.Project.Tracks.Remove(_track);
    }
}
