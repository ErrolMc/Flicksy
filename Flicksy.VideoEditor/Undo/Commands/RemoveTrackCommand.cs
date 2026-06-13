using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;
using System.Collections.ObjectModel;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Removes one <see cref="Track"/> from the project's track list — the inverse of
/// <see cref="AddTrackCommand"/>, pushed by <see cref="TimelineViewModel.RemoveTrack"/> when the
/// user deletes a track from its header.
/// <para>
/// The track is already out of <see cref="Project.Project.Tracks"/> when this is pushed (the edit
/// mutated live), matching the "push after mutation" convention. The removed <see cref="Track"/>
/// instance keeps its <see cref="Track.Clips"/> / <see cref="Track.Transitions"/> while detached, so
/// <see cref="Undo"/> re-inserting the same instance at its captured index brings the whole track
/// (clips included) back in one step — no per-clip snapshot needed. Selection is untouched: the VM
/// drops any selection that pointed at this track's clips before removal, and undo doesn't restore it
/// (matching the coarse grain of the other timeline ops).
/// </para>
/// </summary>
public sealed class RemoveTrackCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly Track _track;
    private readonly int _index;

    public RemoveTrackCommand(TimelineViewModel viewModel, Track track, int index)
    {
        _viewModel = viewModel;
        _track = track;
        _index = index;
    }

    public void Redo()
    {
        _viewModel.Project.Tracks.Remove(_track);
    }

    public void Undo()
    {
        ObservableCollection<Track> tracks = _viewModel.Project.Tracks;
        int index = _index < 0 || _index > tracks.Count ? tracks.Count : _index;
        tracks.Insert(index, _track);
    }
}
