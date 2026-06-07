using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Flicksy.Drawing.Undo;
using Flicksy.Drawing.Undo.Commands;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.Undo;
using Flicksy.VideoEditor.Undo.Commands;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Interaction.Tools;

/// <summary>
/// Body / edge clicks on a clip: click-selection plus drag-to-move (#12 phase 3). A plain click
/// selects the hit clip; <c>Ctrl</c>-click toggles it in the multi-selection set (no drag). A drag
/// past the system threshold retimes the clip live:
/// <list type="bullet">
///   <item><description><b>Single</b>: snap the start edge + clamp into the nearest free gap via
///     <see cref="TimelineViewModel.Snap"/> (excluding the dragged clip); vertical drag onto another
///     <em>same-kind, unlocked</em> track moves the clip between lanes (a different-kind / locked
///     target is refused).</description></item>
///   <item><description><b>Multi</b>: a rigid group — every selected clip shifts by one frame delta
///     (spacing preserved), the anchor's start snaps, and the delta is clamped against non-selected
///     clips (<see cref="TimelineViewModel.ClampGroupDelta"/>). Cross-track is single-only.</description></item>
/// </list>
/// The gesture mutates state live and pushes the undo command(s) on pointer-up (only if changed);
/// <c>Esc</c> reverts. Edges route here until the Trim tool lands in phase 4, so an edge drag moves
/// like a body drag for now. All placement math lives on <see cref="TimelineViewModel"/> so it
/// unit-tests headless against a real VM (ADR 0007); locked tracks report no clip, so this tool
/// never engages there (ADR 0006).
/// </summary>
public sealed class MoveTool : ITimelineTool
{
    private readonly ITimelineSurface _surface;
    private readonly TimelineViewModel _viewModel;

    // Gesture state, valid between OnPointerDown and OnPointerUp/Cancel.
    private readonly List<(Clip Clip, int OriginalStart)> _moved = new();
    private readonly HashSet<Clip> _movedSet = new();
    private bool _active;
    private bool _dragStarted;
    private bool _isSingle;
    private Point _grabPoint;
    private Clip? _anchorClip;
    private int _anchorOriginalStart;
    private Track? _originalTrack;
    private Track? _currentTrack;

    public MoveTool(ITimelineSurface surface, TimelineViewModel viewModel)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public bool IsActive => _active;

    public bool OnPointerDown(Point point, TimelineHit hit, MouseButtonEventArgs e)
    {
        if (hit.Clip is null) 
            return false;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl)
        {
            // Ctrl-click is a selection toggle, not a move — resolve it and don't arm a drag.
            _viewModel.ToggleSelection(hit.Clip);
            return true;
        }

        // Clicking a clip that's already part of a multi-selection drags the whole group and
        // leaves the selection intact; clicking anything else selects it alone first.
        if (!(_viewModel.SelectedClips.Contains(hit.Clip) && _viewModel.SelectedClips.Count > 1))
        {
            _viewModel.SelectedClip = hit.Clip;
        }

        _originalTrack = _viewModel.FindTrack(hit.Clip);
        if (_originalTrack is null) 
            return true;   // not on a track (shouldn't happen) — just select

        _anchorClip = hit.Clip;
        _anchorOriginalStart = hit.Clip.TimelineStart;
        _currentTrack = _originalTrack;
        _grabPoint = point;

        _moved.Clear();
        _movedSet.Clear();
        foreach (Clip clip in _viewModel.SelectedClips)
        {
            _moved.Add((clip, clip.TimelineStart));
            _movedSet.Add(clip);
        }
        _isSingle = _moved.Count == 1;

        _dragStarted = false;
        _active = true;
        _surface.CapturePointer();
        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        if (!_active || _anchorClip is null || _currentTrack is null) 
            return;

        if (!_dragStarted)
        {
            if (Math.Abs(point.X - _grabPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _grabPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }
            _dragStarted = true;
        }

        double ppf = _surface.PixelsPerFrame;
        if (ppf <= 0) 
            return;

        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        int deltaFrames = (int)Math.Round((point.X - _grabPoint.X) / ppf);

        if (_isSingle)
        {
            MoveSingle(point, Math.Max(0, _anchorOriginalStart + deltaFrames), alt);
        }
        else
        {
            MoveGroup(deltaFrames, alt);
        }
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        if (!_active) 
            return;

        _surface.ReleasePointer();
        if (_dragStarted) 
            CommitMove();

        ResetGesture();
    }

    public void OnPointerHover(Point point, TimelineHit hit, MouseEventArgs e)
    {
        // Move grab affordance over a clip body. Cursor management migrated off ClipView into the
        // tools so the Trim tool's resize cursor on edges isn't shadowed by a clip-wide cursor (a
        // child's Cursor wins WPF's QueryCursor). The body routes here; edges route to TrimTool.
        _surface.Cursor = hit.Clip is not null ? Cursors.Hand : null;
    }

    public void Cancel()
    {
        if (!_active) 
            return;

        _surface.ReleasePointer();
        if (_dragStarted) 
            RevertMove();

        ResetGesture();
    }

    private void MoveSingle(Point point, int desiredStart, bool alt)
    {
        Clip clip = _anchorClip!;

        // Resolve the track under the cursor; fall back to the current track when the target is
        // off-stack, locked, or a different kind (cross-track move is same-kind only).
        Track? targetTrack = _surface.HitTest(point).Track;
        if (targetTrack is null || !_viewModel.CanMoveToTrack(clip, targetTrack))
        {
            targetTrack = _currentTrack;
        }

        int clampedStart = _viewModel.Snap(desiredStart, targetTrack!, clip.Duration, alt, _movedSet);

        if (!ReferenceEquals(targetTrack, _currentTrack))
        {
            _viewModel.MoveClipToTrack(clip, targetTrack!, clampedStart);
            _currentTrack = targetTrack;
        }
        else
        {
            clip.TimelineStart = clampedStart;
        }
    }

    private void MoveGroup(int deltaFrames, bool alt)
    {
        int delta = deltaFrames;
        if (!alt)
        {
            // Snap the grabbed clip's start edge to a static target; the whole group inherits the
            // resulting delta so spacing is preserved.
            int snappedAnchor = _viewModel.SnapStartEdge(_anchorOriginalStart + delta, _movedSet);
            delta = snappedAnchor - _anchorOriginalStart;
        }

        delta = _viewModel.ClampGroupDelta(_moved, delta);
        foreach ((Clip clip, int originalStart) in _moved)
        {
            clip.TimelineStart = Math.Max(0, originalStart + delta);
        }
    }

    private void CommitMove()
    {
        Clip clip = _anchorClip!;
        if (_isSingle)
        {
            Track finalTrack = _currentTrack!;
            int finalStart = clip.TimelineStart;
            if (!ReferenceEquals(finalTrack, _originalTrack))
            {
                _viewModel.History.Push(new MoveClipBetweenTracksCommand(
                    _viewModel, clip, _originalTrack!, _anchorOriginalStart, finalTrack, finalStart));
            }
            else if (finalStart != _anchorOriginalStart)
            {
                _viewModel.History.Push(new MoveClipCommand(_viewModel, clip, _anchorOriginalStart, finalStart));
            }
            return;
        }

        var children = new List<IUndoableCommand>();
        foreach ((Clip c, int originalStart) in _moved)
        {
            if (c.TimelineStart != originalStart)
            {
                children.Add(new MoveClipCommand(_viewModel, c, originalStart, c.TimelineStart));
            }
        }
        if (children.Count > 0)
        {
            _viewModel.History.Push(new CompositeCommand(children, new TimelineSelectionScope(_viewModel)));
        }
    }

    private void RevertMove()
    {
        Clip clip = _anchorClip!;
        if (_isSingle)
        {
            if (!ReferenceEquals(_currentTrack, _originalTrack) && _originalTrack is not null)
            {
                _viewModel.MoveClipToTrack(clip, _originalTrack, _anchorOriginalStart);
            }
            else
            {
                clip.TimelineStart = _anchorOriginalStart;
            }
            return;
        }

        foreach ((Clip c, int originalStart) in _moved)
        {
            c.TimelineStart = originalStart;
        }
    }

    private void ResetGesture()
    {
        _active = false;
        _dragStarted = false;
        _isSingle = false;
        _anchorClip = null;
        _originalTrack = null;
        _currentTrack = null;
        _moved.Clear();
        _movedSet.Clear();
    }
}
