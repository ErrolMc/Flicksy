using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Moves one <see cref="Track"/> from <c>fromIndex</c> to <c>toIndex</c> in the project's track list —
/// pushed by <see cref="TimelineViewModel.MoveTrackUp"/> / <see cref="TimelineViewModel.MoveTrackDown"/>
/// (the header's "Move track up / down" commands). The VM only ever swaps a track with its same-kind
/// neighbour (a one-slot move), so the Video → Overlay → Audio banding is preserved; this command just
/// records the two indices and reverses them on undo.
/// <para>
/// The track is already at <c>toIndex</c> when this is pushed (the edit mutated live), matching the
/// "push after mutation" convention — <see cref="Redo"/> runs only when stepping forward through the
/// redo stack. <see cref="System.Collections.ObjectModel.ObservableCollection{T}.Move"/> preserves the
/// same <see cref="Track"/> instance (and its realized header / lane container), so the clips ride
/// along and no per-clip snapshot is needed. Selection is untouched (tracks aren't selectable).
/// </para>
/// </summary>
public sealed class MoveTrackCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly int _fromIndex;
    private readonly int _toIndex;

    public MoveTrackCommand(TimelineViewModel viewModel, int fromIndex, int toIndex)
    {
        _viewModel = viewModel;
        _fromIndex = fromIndex;
        _toIndex = toIndex;
    }

    public void Redo()
    {
        _viewModel.Project.Tracks.Move(_fromIndex, _toIndex);
    }

    public void Undo()
    {
        _viewModel.Project.Tracks.Move(_toIndex, _fromIndex);
    }
}
