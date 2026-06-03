using System;
using System.Windows;
using System.Windows.Input;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Interaction.Tools;

/// <summary>
/// Razor mode (#12 phase 5): the selectable-mode tool that occupies the router's
/// <c>SelectedModeTool</c> slot (engaged by <c>C</c> / the razor toggle). A click cuts the clicked
/// clip at the click point — distinct from <c>S</c> / the scissor button, which split the whole
/// selection at the playhead. The cut delegates to <see cref="TimelineViewModel.SplitClipAt"/>, where
/// all the source-time math and eligibility live, so this tool is a thin pointer adapter.
/// <para>
/// MediaClip-only in v1 (GraphicsClip cut is deferred with the rest of #13); a click on empty lane
/// space or a non-MediaClip is consumed without effect so razor mode never falls through to
/// select / scrub. Resolves fully on pointer-down — no drag, so <see cref="IsActive"/> is always
/// false and <see cref="Cancel"/> is a no-op. Hover shows a crosshair over a cuttable clip.
/// </para>
/// </summary>
public sealed class RazorTool : ITimelineTool
{
    private readonly ITimelineSurface _surface;
    private readonly TimelineViewModel _viewModel;

    public RazorTool(ITimelineSurface surface, TimelineViewModel viewModel)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public bool IsActive => false;

    public bool OnPointerDown(Point point, TimelineHit hit, MouseButtonEventArgs e)
    {
        if (hit.Clip is MediaClip clip)
        {
            _viewModel.SplitClipAt(clip, hit.Frame);
        }

        // Consume even a miss so razor mode never falls through to select / scrub.
        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        // Cut resolves on pointer-down — no drag.
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        // Cut resolves on pointer-down — no drag.
    }

    public void OnPointerHover(Point point, TimelineHit hit, MouseEventArgs e)
    {
        // Crosshair over a cuttable clip; default arrow over empty lane space or an inert track.
        _surface.Cursor = hit.Clip is MediaClip ? Cursors.Cross : null;
    }

    public void Cancel()
    {
        // Nothing to revert — the cut resolves on pointer-down.
    }
}
