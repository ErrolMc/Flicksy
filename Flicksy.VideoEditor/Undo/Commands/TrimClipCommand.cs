using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Single-edge trim of one <see cref="MediaClip"/>: records the clip's <see cref="TrimResult"/>
/// (TimelineStart + SourceIn/SourceOut) before and after the gesture. Pushed on pointer-up only
/// when something changed (the gesture mutates state live, per the editor's undo convention).
/// Re-selects the clip on undo/redo so the user sees what changed — mirrors
/// <see cref="MoveClipCommand"/>. Trim is single-clip, so this is never bundled in a
/// <c>CompositeCommand</c>. <see cref="MediaClip.Duration"/> recomputes from the restored source
/// range, so it isn't recorded separately.
/// </summary>
public sealed class TrimClipCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly MediaClip _clip;
    private readonly TrimResult _before;
    private readonly TrimResult _after;

    public TrimClipCommand(TimelineViewModel viewModel, MediaClip clip, TrimResult before, TrimResult after)
    {
        _viewModel = viewModel;
        _clip = clip;
        _before = before;
        _after = after;
    }

    public void Redo() => Apply(_after);

    public void Undo() => Apply(_before);

    private void Apply(TrimResult state)
    {
        _clip.SourceIn = state.SourceIn;
        _clip.SourceOut = state.SourceOut;
        _clip.TimelineStart = state.TimelineStart;
        _viewModel.SelectedClip = _clip;
    }
}
