using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Single-edge trim of one <see cref="GraphicsClip"/> (#13): records the clip's
/// <see cref="GraphicsTrimResult"/> (TimelineStart + DurationFrames) before and after the gesture.
/// The graphics sibling of <see cref="TrimClipCommand"/> — a pure time-window edit with no source
/// range. Pushed on pointer-up only when something changed; re-selects the clip on undo/redo.
/// Single-clip, never bundled.
/// </summary>
public sealed class GraphicsTrimClipCommand : IUndoableCommand
{
    private readonly TimelineViewModel _viewModel;
    private readonly GraphicsClip _clip;
    private readonly GraphicsTrimResult _before;
    private readonly GraphicsTrimResult _after;

    public GraphicsTrimClipCommand(TimelineViewModel viewModel, GraphicsClip clip, GraphicsTrimResult before, GraphicsTrimResult after)
    {
        _viewModel = viewModel;
        _clip = clip;
        _before = before;
        _after = after;
    }

    public void Redo() => Apply(_after);

    public void Undo() => Apply(_before);

    private void Apply(GraphicsTrimResult state)
    {
        _clip.TimelineStart = state.TimelineStart;
        _clip.DurationFrames = state.DurationFrames;
        _viewModel.SelectedClip = _clip;
    }
}
