using System;
using System.Windows;
using System.Windows.Input;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Interaction.Tools;

/// <summary>
/// Empty-lane clicks. In this slice (#12 phase 2) a plain click on empty lane space clears the
/// selection (the click-to-deselect behaviour migrated off <c>TimelineView</c>'s root handler);
/// <c>Ctrl</c>-click leaves the selection alone (a missed additive click shouldn't wipe it). The
/// cross-track rubber-band drag that selects intersecting clips lands in phase 6, hung off the
/// same <see cref="OnPointerDown"/>.
/// <para>
/// Depends only on <see cref="ITimelineSurface"/> + <see cref="TimelineViewModel"/> (ADR 0007).
/// </para>
/// </summary>
public sealed class MarqueeTool : ITimelineTool
{
    private readonly ITimelineSurface _surface;
    private readonly TimelineViewModel _viewModel;

    public MarqueeTool(ITimelineSurface surface, TimelineViewModel viewModel)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    // No rubber-band gesture yet (phase 6). The click is fully resolved on pointer-down.
    public bool IsActive => false;

    public bool OnPointerDown(Point point, TimelineHit hit, MouseButtonEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (!ctrl)
        {
            _viewModel.SelectedClip = null;
        }

        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        // Rubber-band selection lands in phase 6.
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        // Rubber-band selection lands in phase 6.
    }

    public void OnPointerHover(Point point, TimelineHit hit, MouseEventArgs e)
    {
        // No hover affordance over empty lane space.
    }
}
