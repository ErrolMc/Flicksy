using System;
using System.Windows;
using System.Windows.Input;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.Undo.Commands;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Interaction.Tools;

/// <summary>
/// Left/right edge drag to trim a clip's in/out point (#12 phase 4). Grabbing an edge and dragging
/// retimes that edge live, holding the opposite edge fixed: the timeline delta maps into source
/// time via the clip's <see cref="MediaClip.Speed"/>, advancing <see cref="MediaClip.SourceIn"/>
/// (left edge — which also slides <see cref="Clip.TimelineStart"/>) or
/// <see cref="MediaClip.SourceOut"/> (right edge). All clamping — neighbour edge, source bounds,
/// 1-frame minimum, broken-clip shrink-only — lives in <see cref="TimelineViewModel.ResolveTrim"/>
/// so it unit-tests headless (ADR 0007). The gesture pushes a <see cref="TrimClipCommand"/> on
/// pointer-up only if the clip changed; <c>Esc</c> reverts. Trim is single-clip and never moves a
/// neighbour (ADR 0006). Hover sets a horizontal-resize cursor over a trimmable edge.
/// <para>
/// Both clip kinds trim. A <see cref="MediaClip"/> maps the edge into source time (the math above);
/// a <see cref="GraphicsClip"/> is a pure time-window edit via
/// <see cref="TimelineViewModel.ResolveGraphicsTrim"/> + <c>GraphicsTrimClipCommand</c> (#13).
/// </para>
/// </summary>
public sealed class TrimTool : ITimelineTool
{
    private readonly ITimelineSurface _surface;
    private readonly TimelineViewModel _viewModel;

    // Gesture state, valid between OnPointerDown and OnPointerUp/Cancel.
    private bool _active;
    private bool _dragStarted;
    private bool _fromLeft;
    private Clip? _clip;
    private Point _grabPoint;
    private int _originalEdgeFrame;   // start (left edge) or end (right edge) at gesture start
    private TrimResult _before;               // MediaClip before-snapshot
    private GraphicsTrimResult _graphicsBefore;

    public TrimTool(ITimelineSurface surface, TimelineViewModel viewModel)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public bool IsActive => _active;

    public bool OnPointerDown(Point point, TimelineHit hit, MouseButtonEventArgs e)
    {
        // Edges only; both MediaClip and GraphicsClip trim (each resolves on its own state).
        if (hit.Clip is not (MediaClip or GraphicsClip))
            return false;

        if (hit.Zone is not (HitZone.LeftEdge or HitZone.RightEdge))
            return false;

        Clip clip = hit.Clip!;
        _clip = clip;
        _fromLeft = hit.Zone == HitZone.LeftEdge;
        _grabPoint = point;
        _originalEdgeFrame = _fromLeft ? clip.TimelineStart : clip.TimelineStart + clip.Duration;
        if (clip is MediaClip media)
            _before = TrimResult.Capture(media);
        else if (clip is GraphicsClip graphics)
            _graphicsBefore = GraphicsTrimResult.Capture(graphics);

        // Trimming acts on (and selects) the single grabbed clip, so the inspector follows it.
        _viewModel.SelectedClip = clip;

        _dragStarted = false;
        _active = true;
        _surface.CapturePointer();
        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        if (!_active || _clip is null) 
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

        int deltaFrames = (int)Math.Round((point.X - _grabPoint.X) / ppf);
        int desiredEdgeFrame = _originalEdgeFrame + deltaFrames;
        if (_clip is MediaClip media)
            ApplyMedia(media, _viewModel.ResolveTrim(media, _fromLeft, desiredEdgeFrame));
        else if (_clip is GraphicsClip graphics)
            ApplyGraphics(graphics, _viewModel.ResolveGraphicsTrim(graphics, _fromLeft, desiredEdgeFrame));
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        if (!_active) 
            return;

        _surface.ReleasePointer();
        if (_dragStarted) 
            CommitTrim();

        ResetGesture();
    }

    public void OnPointerHover(Point point, TimelineHit hit, MouseEventArgs e)
    {
        // Resize cursor over a trimmable clip edge (Media or Graphics); default over anything else.
        bool onEdge = hit.Clip is (MediaClip or GraphicsClip) && hit.Zone is (HitZone.LeftEdge or HitZone.RightEdge);
        _surface.Cursor = onEdge ? Cursors.SizeWE : null;
    }

    public void Cancel()
    {
        if (!_active)
            return;

        _surface.ReleasePointer();
        if (_dragStarted)
        {
            // Revert the live mutation.
            if (_clip is MediaClip media)
                ApplyMedia(media, _before);
            else if (_clip is GraphicsClip graphics)
                ApplyGraphics(graphics, _graphicsBefore);
        }

        ResetGesture();
    }

    private static void ApplyMedia(MediaClip clip, TrimResult state)
    {
        clip.SourceIn = state.SourceIn;
        clip.SourceOut = state.SourceOut;
        clip.TimelineStart = state.TimelineStart;
    }

    private static void ApplyGraphics(GraphicsClip clip, GraphicsTrimResult state)
    {
        clip.TimelineStart = state.TimelineStart;
        clip.DurationFrames = state.DurationFrames;
    }

    private void CommitTrim()
    {
        if (_clip is MediaClip media)
        {
            TrimResult after = TrimResult.Capture(media);
            if (after != _before)
                _viewModel.History.Push(new TrimClipCommand(_viewModel, media, _before, after));
        }
        else if (_clip is GraphicsClip graphics)
        {
            GraphicsTrimResult after = GraphicsTrimResult.Capture(graphics);
            if (after != _graphicsBefore)
                _viewModel.History.Push(new GraphicsTrimClipCommand(_viewModel, graphics, _graphicsBefore, after));
        }
    }

    private void ResetGesture()
    {
        _active = false;
        _dragStarted = false;
        _clip = null;
    }
}
