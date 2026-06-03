using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Same-track retime of a single clip: records the <see cref="Clip.TimelineStart"/> before and
/// after a move gesture. Pushed on pointer-up only when the start actually changed (the gesture
/// mutates state live, per the editor's undo convention). Re-selects the moved clip on redo/undo
/// so the user sees what changed — mirrors <c>Flicksy.Drawing.Undo.Commands.TransformCommand</c>.
/// Cross-track moves use <see cref="MoveClipBetweenTracksCommand"/>; multi-move bundles several of
/// these in a <c>CompositeCommand</c>.
/// </summary>
public sealed class MoveClipCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly Clip _clip;
    private readonly int _before;
    private readonly int _after;

    public MoveClipCommand(TimelineViewModel viewModel, Clip clip, int before, int after)
    {
        _viewModel = viewModel;
        _clip = clip;
        _before = before;
        _after = after;
    }

    public void Redo()
    {
        _clip.TimelineStart = _after;
        _viewModel.SelectedClip = _clip;
    }

    public void Undo()
    {
        _clip.TimelineStart = _before;
        _viewModel.SelectedClip = _clip;
    }
}
