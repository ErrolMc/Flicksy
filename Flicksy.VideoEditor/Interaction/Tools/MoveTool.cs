using System;
using System.Windows;
using System.Windows.Input;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Interaction.Tools;

/// <summary>
/// Body / edge clicks on a clip. In this slice (#12 phase 2) the tool does click-selection
/// only — plain click selects the hit clip, <c>Ctrl</c>-click toggles it in the multi-selection
/// set. Drag-to-move (with snap + non-destructive gap clamp + rigid multi-group) lands in phase
/// 3, hung off the same <see cref="OnPointerDown"/> capture; edges route here until the Trim
/// tool lands in phase 4, so an edge click selects like a body click for now.
/// <para>
/// Depends only on <see cref="ITimelineSurface"/> + <see cref="TimelineViewModel"/> so its
/// gesture math (phase 3) unit-tests against a fake surface (ADR 0007). Locked tracks are inert
/// (ADR 0006): the hit-tester reports no clip on them, so this tool never engages there.
/// </para>
/// </summary>
public sealed class MoveTool : ITimelineTool
{
    private readonly ITimelineSurface _surface;
    private readonly TimelineViewModel _viewModel;

    public MoveTool(ITimelineSurface surface, TimelineViewModel viewModel)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    // No drag gesture yet (phase 3). Selection is fully resolved on pointer-down.
    public bool IsActive => false;

    public bool OnPointerDown(Point point, TimelineHit hit, MouseButtonEventArgs e)
    {
        if (hit.Clip is null) return false;

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl)
        {
            _viewModel.ToggleSelection(hit.Clip);
        }
        else
        {
            _viewModel.SelectedClip = hit.Clip;
        }

        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        // Drag-to-move lands in phase 3.
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        // Drag-to-move lands in phase 3.
    }

    public void OnPointerHover(Point point, TimelineHit hit, MouseEventArgs e)
    {
        // ClipView sets a Hand cursor over its body; no extra affordance needed in this slice.
    }
}
