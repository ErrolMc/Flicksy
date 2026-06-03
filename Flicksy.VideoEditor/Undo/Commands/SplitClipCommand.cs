using System;
using System.Collections.Generic;
using System.Linq;
using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Splits one <see cref="MediaClip"/> into two adjacent halves at a timeline frame (#12 phase 5).
/// The original clip is kept as the <b>left</b> half — its <see cref="MediaClip.SourceOut"/> is
/// pulled back to the split point — while a new <see cref="MediaClip"/> takes the remainder of the
/// source range as the right half, inheriting the original's other properties. Any transition on
/// the original's right edge reassigns to the right half via
/// <see cref="Track.ReassignTransitionsForSplit"/>; the track's whole transition list is snapshotted
/// before/after so undo restores it exactly (empty until #14 — forward-looking integrity).
/// <para>
/// Per the editor's undo convention the VM performs the split live and pushes this; <see cref="Redo"/>
/// re-applies the after-state and <see cref="Undo"/> restores the before-state. Re-selects the (left)
/// original on both, mirroring <see cref="MoveClipCommand"/>. MediaClip-only in v1 (GraphicsClip split
/// is #13); multi-split bundles several of these in a <c>CompositeCommand</c>.
/// </para>
/// </summary>
public sealed class SplitClipCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly Track _track;
    private readonly MediaClip _left;
    private readonly MediaClip _right;
    private readonly TimeSpan _leftSourceOutBefore;
    private readonly TimeSpan _splitSourceTime;
    private readonly List<Transition> _transitionsBefore;
    private readonly List<Transition> _transitionsAfter;

    public SplitClipCommand(
        TimelineViewModel viewModel,
        Track track,
        MediaClip left,
        MediaClip right,
        TimeSpan leftSourceOutBefore,
        TimeSpan splitSourceTime,
        IReadOnlyList<Transition> transitionsBefore,
        IReadOnlyList<Transition> transitionsAfter)
    {
        _viewModel = viewModel;
        _track = track;
        _left = left;
        _right = right;
        _leftSourceOutBefore = leftSourceOutBefore;
        _splitSourceTime = splitSourceTime;
        _transitionsBefore = transitionsBefore.ToList();
        _transitionsAfter = transitionsAfter.ToList();
    }

    public void Redo()
    {
        _left.SourceOut = _splitSourceTime;
        if (!_track.Clips.Contains(_right))
        {
            _viewModel.InsertClipSorted(_track, _right);
        }
        _track.ReplaceTransitions(_transitionsAfter);
        _viewModel.SelectedClip = _left;
    }

    public void Undo()
    {
        _track.Clips.Remove(_right);
        _left.SourceOut = _leftSourceOutBefore;
        _track.ReplaceTransitions(_transitionsBefore);
        _viewModel.SelectedClip = _left;
    }
}
