using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Moves a single clip to a different track (same <see cref="TrackKind"/>; the cross-track guard
/// lives in <see cref="TimelineViewModel.CanMoveToTrack"/>) and retimes it. Records the source
/// track + start and the destination track + start. Both directions drive through
/// <see cref="TimelineViewModel.MoveClipToTrack"/>, which removes the clip from whichever track
/// currently holds it and re-inserts sorted — so redo/undo are correct regardless of where the
/// clip sits when invoked. Cross-track is single-selection only (#12), so this is never bundled.
/// </summary>
public sealed class MoveClipBetweenTracksCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly Clip _clip;
    private readonly Track _fromTrack;
    private readonly int _beforeStart;
    private readonly Track _toTrack;
    private readonly int _afterStart;

    public MoveClipBetweenTracksCommand(
        TimelineViewModel viewModel,
        Clip clip,
        Track fromTrack,
        int beforeStart,
        Track toTrack,
        int afterStart)
    {
        _viewModel = viewModel;
        _clip = clip;
        _fromTrack = fromTrack;
        _beforeStart = beforeStart;
        _toTrack = toTrack;
        _afterStart = afterStart;
    }

    public void Redo()
    {
        _viewModel.MoveClipToTrack(_clip, _toTrack, _afterStart);
        _viewModel.SelectedClip = _clip;
    }

    public void Undo()
    {
        _viewModel.MoveClipToTrack(_clip, _fromTrack, _beforeStart);
        _viewModel.SelectedClip = _clip;
    }
}
