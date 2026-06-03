using System.Collections.Generic;
using System.Linq;
using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Removes one <see cref="Clip"/> from its <see cref="Track"/> (#12 phase 5; generic on the
/// <see cref="Clip"/> base — Media and Graphics alike). Any transition the clip participated in is
/// removed with it (adjacency broken — ADR 0006); the track's transition list is snapshotted
/// before/after for undo (empty until #14). Non-destructive: the vacated span is left as a gap and
/// nothing else on the track shifts.
/// <para>
/// <see cref="Undo"/> re-inserts the clip in TimelineStart order (it kept its start, so it lands back
/// in place) and re-selects it; <see cref="Redo"/> removes it again and drops it from the selection.
/// Multi-delete bundles several of these in a <c>CompositeCommand</c> with a <c>TimelineSelectionScope</c>.
/// </para>
/// </summary>
public sealed class RemoveClipCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly Track _track;
    private readonly Clip _clip;
    private readonly List<Transition> _transitionsBefore;
    private readonly List<Transition> _transitionsAfter;

    public RemoveClipCommand(
        TimelineViewModel viewModel,
        Track track,
        Clip clip,
        IReadOnlyList<Transition> transitionsBefore,
        IReadOnlyList<Transition> transitionsAfter)
    {
        _viewModel = viewModel;
        _track = track;
        _clip = clip;
        _transitionsBefore = transitionsBefore.ToList();
        _transitionsAfter = transitionsAfter.ToList();
    }

    public void Redo()
    {
        _track.Clips.Remove(_clip);
        _track.ReplaceTransitions(_transitionsAfter);
        _viewModel.Deselect(_clip);
    }

    public void Undo()
    {
        _viewModel.InsertClipSorted(_track, _clip);
        _track.ReplaceTransitions(_transitionsBefore);
        _viewModel.SelectedClip = _clip;
    }
}
