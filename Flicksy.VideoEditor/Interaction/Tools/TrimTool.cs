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
/// <b>v1 is <see cref="MediaClip"/>-only</b> — the source-range mapping is media-specific. No
/// <see cref="GraphicsClip"/> reaches a real timeline yet, so its edge trim is deferred with the
/// rest of graphics-clip editing (#13); a graphics-clip edge simply doesn't engage here.
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
    private MediaClip? _clip;
    private Point _grabPoint;
    private int _originalEdgeFrame;   // start (left edge) or end (right edge) at gesture start
    private TrimResult _before;

    public TrimTool(ITimelineSurface surface, TimelineViewModel viewModel)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public bool IsActive => _active;

    public bool OnPointerDown(Point point, TimelineHit hit, MouseButtonEventArgs e)
    {
        // Edges only, MediaClip only (see the class note on the GraphicsClip deferral).
        if (hit.Clip is not MediaClip clip) 
            return false;

        if (hit.Zone is not (HitZone.LeftEdge or HitZone.RightEdge)) 
            return false;

        _clip = clip;
        _fromLeft = hit.Zone == HitZone.LeftEdge;
        _grabPoint = point;
        _originalEdgeFrame = _fromLeft ? clip.TimelineStart : clip.TimelineStart + clip.Duration;
        _before = TrimResult.Capture(clip);

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
        TrimResult result = _viewModel.ResolveTrim(_clip, _fromLeft, _originalEdgeFrame + deltaFrames);
        Apply(result);
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
        // Resize cursor over a trimmable (MediaClip) edge; default over anything else routed here
        // (e.g. a GraphicsClip edge, which v1 can't trim).
        bool onEdge = hit.Clip is MediaClip && hit.Zone is (HitZone.LeftEdge or HitZone.RightEdge);
        _surface.Cursor = onEdge ? Cursors.SizeWE : null;
    }

    public void Cancel()
    {
        if (!_active)
            return;

        _surface.ReleasePointer();
        if (_dragStarted) 
            Apply(_before);   // revert the live mutation

        ResetGesture();
    }

    private void Apply(TrimResult state)
    {
        MediaClip clip = _clip!;
        clip.SourceIn = state.SourceIn;
        clip.SourceOut = state.SourceOut;
        clip.TimelineStart = state.TimelineStart;
    }

    private void CommitTrim()
    {
        TrimResult after = TrimResult.Capture(_clip!);
        if (after != _before)
        {
            _viewModel.History.Push(new TrimClipCommand(_viewModel, _clip!, _before, after));
        }
    }

    private void ResetGesture()
    {
        _active = false;
        _dragStarted = false;
        _clip = null;
    }
}
