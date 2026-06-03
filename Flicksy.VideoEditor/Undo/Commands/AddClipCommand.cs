using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Adds one <see cref="Clip"/> to a <see cref="Track"/> — the undo entry for a media-bin drag-drop
/// onto the timeline (the mirror of <see cref="RemoveClipCommand"/>). A freshly dropped clip
/// participates in no transitions, so none are snapshotted; non-destructive, since the clip keeps its
/// TimelineStart and nothing else on the track shifts.
/// <para>
/// The clip is already in the track when this command is pushed (the drop mutated live), matching the
/// "push after mutation" convention — <see cref="Redo"/> runs only when stepping forward through the
/// redo stack. <see cref="Undo"/> removes the clip and drops it from the selection; <see cref="Redo"/>
/// re-inserts it in TimelineStart order (it kept its start, so it lands back in place) and re-selects it.
/// </para>
/// </summary>
public sealed class AddClipCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly Track _track;
    private readonly Clip _clip;

    public AddClipCommand(TimelineViewModel viewModel, Track track, Clip clip)
    {
        _viewModel = viewModel;
        _track = track;
        _clip = clip;
    }

    public void Redo()
    {
        _viewModel.InsertClipSorted(_track, _clip);
        _viewModel.SelectedClip = _clip;
    }

    public void Undo()
    {
        _track.Clips.Remove(_clip);
        _viewModel.Deselect(_clip);
    }
}
