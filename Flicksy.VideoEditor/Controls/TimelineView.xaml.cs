using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flicksy.VideoEditor.Controls.Timeline;
using Flicksy.VideoEditor.Interaction;
using Flicksy.VideoEditor.Interaction.Tools;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Controls;

/// <summary>
/// Center-column timeline surface. <c>DataContext</c> is
/// <see cref="ViewModels.TimelineViewModel"/>. Layout is a 2×2 grid with three
/// <see cref="ScrollViewer"/>s: a top ruler scroller, a left pinned-headers scroller, and
/// a main lanes scroller in the bottom-right. The main scroller owns the visible
/// scrollbars; the other two have hidden scrollbars and are slaved to its H/V offsets via
/// <see cref="OnMainScrollerScrollChanged"/>. Wheel handler attaches to the outer border so
/// it fires over any sub-scroller (plain = H-pan, Shift = V-pan, Ctrl = zoom on playhead).
/// <para>
/// This control is the <see cref="ITimelineSurface"/> host for the interaction layer (ADR
/// 0007): it owns a <see cref="TimelineToolRouter"/> + the hit-zone tools, rebuilt when the
/// bound VM changes (mirrors <c>DrawingView.OnDataContextChanged</c>). Pointer Preview handlers
/// on <c>LanesHost</c> (the scrolled content, so coordinates are content space) forward to the
/// router; click-select / click-deselect live in the Move / Marquee tools, not here.
/// </para>
/// </summary>
public partial class TimelineView : UserControl, ITimelineSurface
{
    private const double ZoomStep = 1.15;
    private const double PanLinesPerNotch = 3;

    private readonly TimelineToolRouter _toolRouter;
    private MoveTool? _moveTool;
    private MarqueeTool? _marqueeTool;

    public TimelineView()
    {
        InitializeComponent();

        // Hit-zone dispatch (ADR 0007 three-tier): Body → Move, edges → Trim (Move until the
        // Trim tool lands in phase 4), None → Marquee. The router's active-gesture and
        // selected-mode (Razor, phase 5) tiers sit in front of this selector.
        _toolRouter = new TimelineToolRouter(zone => zone switch
        {
            HitZone.Body => _moveTool,
            HitZone.LeftEdge => _moveTool,
            HitZone.RightEdge => _moveTool,
            _ => _marqueeTool,
        });

        DataContextChanged += OnDataContextChanged;
    }

    private TimelineViewModel? ViewModel => DataContext as TimelineViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Tools live as long as the document they operate on; a VM swap means a fresh set.
        if (e.NewValue is TimelineViewModel newVm)
        {
            _moveTool = new MoveTool(this, newVm);
            _marqueeTool = new MarqueeTool(this, newVm);
        }
        else
        {
            _moveTool = null;
            _marqueeTool = null;
        }
    }

    private void OnMainScrollerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Push H/V offsets from MainScroller into the slave scrollers so ruler stays
        // aligned with lanes horizontally and headers stay aligned with lanes vertically.
        if (e.HorizontalChange != 0)
        {
            RulerScroller.ScrollToHorizontalOffset(MainScroller.HorizontalOffset);
        }
        if (e.VerticalChange != 0)
        {
            HeadersScroller.ScrollToVerticalOffset(MainScroller.VerticalOffset);
        }
    }

    // ---------- Interaction layer: Preview pointer forwarding ----------
    // Handlers live on LanesHost (the scrolled content) so e.GetPosition(LanesHost) is already
    // content space. Preview-tunnel so the router sees the down before ClipView does.

    private void OnLanesPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;

        var point = e.GetPosition(LanesHost);
        var hit = HitTest(point);
        if (_toolRouter.OnPointerDown(point, hit, e))
        {
            e.Handled = true;
        }
    }

    private void OnLanesPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel is null) return;

        var point = e.GetPosition(LanesHost);

        if (_toolRouter.HasActiveGesture)
        {
            _toolRouter.OnPointerMove(point, e);
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _toolRouter.OnPointerHover(point, HitTest(point), e);
        }
    }

    private void OnLanesPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_toolRouter.HasActiveGesture)
        {
            _toolRouter.OnPointerUp(e.GetPosition(LanesHost), e);
        }
    }

    private void OnRootPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) return;

        var mods = Keyboard.Modifiers;

        if ((mods & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ZoomCenteredOnPlayhead(vm, e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep);
            e.Handled = true;
            return;
        }

        if ((mods & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            MainScroller.ScrollToVerticalOffset(MainScroller.VerticalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        // Plain wheel = horizontal pan. WPF reports wheel delta in multiples of 120;
        // scale to feel like a normal scroll step.
        MainScroller.ScrollToHorizontalOffset(MainScroller.HorizontalOffset - e.Delta * PanLinesPerNotch / 120.0 * 16);
        e.Handled = true;
    }

    private void ZoomCenteredOnPlayhead(TimelineViewModel vm, double factor)
    {
        // Headers are pinned outside MainScroller, so MainScroller's content coordinate
        // for the playhead is just playhead × PixelsPerFrame (no header offset). Capture
        // the playhead's current screen X, apply the zoom, then re-derive the scroll
        // offset so the playhead lands at the same screen X.
        var oldPxPerFrame = vm.PixelsPerFrame;
        var playhead = vm.Transport.Playhead;

        var oldContentX = playhead * oldPxPerFrame;
        var screenX = oldContentX - MainScroller.HorizontalOffset;

        vm.ZoomBy(factor);

        var newContentX = playhead * vm.PixelsPerFrame;
        var newOffset = Math.Max(0, newContentX - screenX);
        MainScroller.ScrollToHorizontalOffset(newOffset);
    }

    // ---------- ITimelineSurface ----------

    double ITimelineSurface.PixelsPerFrame => ViewModel?.PixelsPerFrame ?? 1.0;

    double ITimelineSurface.TrackHeight => ClipsLaneView.TrackHeight;

    Cursor? ITimelineSurface.Cursor
    {
        get => Cursor;
        set => Cursor = value;
    }

    public TimelineHit HitTest(Point contentPoint)
    {
        var vm = ViewModel;
        if (vm is null) return TimelineHit.Miss;
        return TimelineHitTester.HitTest(
            contentPoint.X,
            contentPoint.Y,
            vm.Project.Tracks,
            vm.PixelsPerFrame,
            ClipsLaneView.TrackHeight);
    }

    public Point GetContentPoint(MouseEventArgs e) => e.GetPosition(LanesHost);

    void ITimelineSurface.CapturePointer() => LanesHost.CaptureMouse();

    void ITimelineSurface.ReleasePointer() => LanesHost.ReleaseMouseCapture();
}
