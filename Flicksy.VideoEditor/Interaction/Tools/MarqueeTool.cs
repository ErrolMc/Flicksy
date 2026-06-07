using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Interaction.Tools;

/// <summary>
/// Empty-lane clicks and the cross-track rubber-band multi-select (#12 phase 6). Engaged by the
/// router for <see cref="HitZone.None"/> (empty lane space, or an inert <c>Locked</c> track).
/// <list type="bullet">
///   <item><description>A plain click (no drag) clears the selection; <c>Ctrl</c>-click leaves it
///     (a missed additive click shouldn't wipe the selection).</description></item>
///   <item><description>A drag past the system threshold paints a rubber-band on the timeline-wide
///     adorner layer and live-selects every clip the band intersects across tracks
///     (<see cref="TimelineHitTester.ClipsIntersecting"/>, which skips <c>Locked</c> tracks). Plain
///     replaces the selection; <c>Ctrl</c> adds to the selection that existed when the drag began.
///     <c>Esc</c> reverts to that selection.</description></item>
/// </list>
/// Selection is not undoable (it mirrors click-select via <see cref="MoveTool"/>, which also just
/// writes the VM). The intersection math lives on <see cref="TimelineHitTester"/> so it unit-tests
/// headless; this tool only orchestrates the gesture. Depends only on
/// <see cref="ITimelineSurface"/> + <see cref="TimelineViewModel"/> (ADR 0007).
/// </summary>
public sealed class MarqueeTool : ITimelineTool
{
    private readonly ITimelineSurface _surface;
    private readonly TimelineViewModel _viewModel;

    // Gesture state, valid between OnPointerDown and OnPointerUp/Cancel.
    private bool _active;
    private bool _dragStarted;
    private bool _additive;
    private Point _anchor;
    private List<Clip> _baseSelection = new();   // selection at gesture start: additive union + Esc-revert target
    private Clip? _basePrimary;
    private HashSet<Clip>? _applied;              // last set pushed to the VM — guards redundant SetSelection churn

    public MarqueeTool(ITimelineSurface surface, TimelineViewModel viewModel)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public bool IsActive => _active;

    public bool OnPointerDown(Point point, TimelineHit hit, MouseButtonEventArgs e)
    {
        _additive = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        _anchor = point;
        _baseSelection = _viewModel.SelectedClips.ToList();
        _basePrimary = _viewModel.SelectedClip;
        _applied = new HashSet<Clip>(_baseSelection);   // nothing applied yet beyond the existing selection
        _dragStarted = false;
        _active = true;

        // Capture so a drag that leaves the timeline bounds keeps delivering move/up (mirrors MoveTool).
        // The plain-click clear / Ctrl-click leave is deferred to pointer-up so a click-drag doesn't
        // clear first and rebuild.
        _surface.CapturePointer();
        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        if (!_active) 
            return;

        if (!_dragStarted)
        {
            if (Math.Abs(point.X - _anchor.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _anchor.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }
            _dragStarted = true;
        }

        Rect rect = BuildRect(_anchor, point);
        _surface.ShowMarquee(rect);
        ApplySelection(rect);
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        if (!_active) 
            return;

        _surface.ReleasePointer();
        _surface.HideMarquee();

        if (_dragStarted)
        {
            // Final commit at the release point (the last move may be a hair stale).
            ApplySelection(BuildRect(_anchor, point));
        }
        else if (!_additive)
        {
            // Plain click on empty space clears; Ctrl-click leaves the selection intact.
            _viewModel.SelectedClip = null;
        }

        ResetGesture();
    }

    public void OnPointerHover(Point point, TimelineHit hit, MouseEventArgs e)
    {
        // Default cursor over empty lane space / inert (locked) tracks. Reset here so moving off a
        // clip body or edge (which set Hand / resize via their tools) restores the arrow.
        _surface.Cursor = null;
    }

    public void Cancel()
    {
        if (!_active) 
            return;

        _surface.ReleasePointer();
        _surface.HideMarquee();
        if (_dragStarted)
        {
            _viewModel.SetSelection(_baseSelection, _basePrimary);   // restore the pre-marquee selection
        }
        ResetGesture();
    }

    // Two corners → a normalized rect (WPF's Point/Point ctor takes min corner + |delta|).
    private static Rect BuildRect(Point a, Point b) => new(a, b);

    private void ApplySelection(Rect rect)
    {
        IReadOnlyList<Clip> hits = TimelineHitTester.ClipsIntersecting(
            rect.Left, rect.Top, rect.Width, rect.Height,
            _viewModel.Project.Tracks, _surface.PixelsPerFrame, _surface.TrackHeight);

        List<Clip> target;
        Clip? primary;
        if (_additive)
        {
            // Union the band's hits onto the selection that existed when the drag began, keeping its
            // primary stable so the right rail / inspector don't thrash mid-drag.
            target = new List<Clip>(_baseSelection);
            foreach (Clip clip in hits)
            {
                if (!target.Contains(clip)) 
                    target.Add(clip);
            }
            primary = _basePrimary;
        }
        else
        {
            target = new List<Clip>(hits);
            primary = null;   // SetSelection picks the first (top-left-most) hit as primary
        }

        if (SelectionUnchanged(target)) 
            return;   // skip redundant rebuilds on moves that don't change membership

        _applied = new HashSet<Clip>(target);
        _viewModel.SetSelection(target, primary);
    }

    private bool SelectionUnchanged(List<Clip> target)
    {
        if (_applied is null || _applied.Count != target.Count) 
            return false;

        foreach (Clip clip in target)
        {
            if (!_applied.Contains(clip)) 
                return false;
        }
        return true;
    }

    private void ResetGesture()
    {
        _active = false;
        _dragStarted = false;
        _additive = false;
        _baseSelection = new List<Clip>();
        _basePrimary = null;
        _applied = null;
    }
}
