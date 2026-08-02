using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Splits one <see cref="GraphicsClip"/> into two adjacent halves at a timeline frame (#13) — the
/// graphics sibling of <see cref="SplitClipCommand"/>. The original is kept as the <b>left</b> half
/// (its <see cref="GraphicsClip.DurationFrames"/> pulled back to the split point); a new clip takes
/// the remaining duration as the right half, wrapping a <see cref="Flicksy.Drawing.Source.DrawingItem.Clone">clone</see>
/// of the same drawing object. No source mapping and no transitions (overlay graphics never carry them),
/// so it stores only the split frame + the original's prior duration.
/// <para>
/// Per the editor's undo convention the VM performs the split live and pushes this; <see cref="Redo"/>
/// re-applies the after-state, <see cref="Undo"/> restores the before-state. Re-selects the (left)
/// original on both. Multi-split bundles several of these in a <c>CompositeCommand</c>.
/// </para>
/// </summary>
public sealed class GraphicsSplitClipCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly Track _track;
    private readonly GraphicsClip _left;
    private readonly GraphicsClip _right;
    private readonly int _leftDurationBefore;
    private readonly int _splitFrame;

    public GraphicsSplitClipCommand(
        TimelineViewModel viewModel,
        Track track,
        GraphicsClip left,
        GraphicsClip right,
        int leftDurationBefore,
        int splitFrame)
    {
        _viewModel = viewModel;
        _track = track;
        _left = left;
        _right = right;
        _leftDurationBefore = leftDurationBefore;
        _splitFrame = splitFrame;
    }

    public void Redo()
    {
        _left.DurationFrames = _splitFrame - _left.TimelineStart;
        if (!_track.Clips.Contains(_right))
        {
            _viewModel.InsertClipSorted(_track, _right);
        }
        _viewModel.SelectedClip = _left;
    }

    public void Undo()
    {
        _track.Clips.Remove(_right);
        _left.DurationFrames = _leftDurationBefore;
        _viewModel.SelectedClip = _left;
    }
}
