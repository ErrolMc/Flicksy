using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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
    private TrimTool? _trimTool;
    private MarqueeTool? _marqueeTool;
    private RazorTool? _razorTool;
    private MarqueeAdorner? _marqueeAdorner;
    private Window? _hookedWindow;

    public TimelineView()
    {
        InitializeComponent();

        // Hit-zone dispatch (ADR 0007 three-tier): Body → Move, edges → Trim, None → Marquee. The
        // router's active-gesture and selected-mode (Razor, phase 5) tiers sit in front of this
        // selector.
        _toolRouter = new TimelineToolRouter(zone => zone switch
        {
            HitZone.Body => _moveTool,
            HitZone.LeftEdge => _trimTool,
            HitZone.RightEdge => _trimTool,
            _ => _marqueeTool,
        });

        DataContextChanged += OnDataContextChanged;
        // Esc-cancel for an in-progress gesture. A captured pointer doesn't capture the keyboard,
        // so listen at the owning window (PreviewKeyDown) rather than on this control, which never
        // holds focus. Kept inside the surface host so the window stays ignorant of timeline tools.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hookedWindow = Window.GetWindow(this);
        if (_hookedWindow is not null)
        {
            _hookedWindow.PreviewKeyDown += OnWindowPreviewKeyDown;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hookedWindow is not null)
        {
            _hookedWindow.PreviewKeyDown -= OnWindowPreviewKeyDown;
            _hookedWindow = null;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        // Esc first cancels an in-progress gesture; otherwise it exits razor mode. A captured pointer
        // doesn't capture the keyboard, so this listens at the owning window, not on this control.
        if (_toolRouter.HasActiveGesture)
        {
            _toolRouter.CancelGesture();
            e.Handled = true;
        }
        else if (ViewModel is { IsRazorMode: true } vm)
        {
            vm.IsRazorMode = false;
            e.Handled = true;
        }
    }

    private TimelineViewModel? ViewModel => DataContext as TimelineViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is TimelineViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // Tools live as long as the document they operate on; a VM swap means a fresh set.
        if (e.NewValue is TimelineViewModel newVm)
        {
            _moveTool = new MoveTool(this, newVm);
            _trimTool = new TrimTool(this, newVm);
            _marqueeTool = new MarqueeTool(this, newVm);
            _razorTool = new RazorTool(this, newVm);
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyRazorMode(newVm.IsRazorMode);
        }
        else
        {
            _moveTool = null;
            _trimTool = null;
            _marqueeTool = null;
            _razorTool = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.IsRazorMode) && sender is TimelineViewModel vm)
        {
            ApplyRazorMode(vm.IsRazorMode);
        }
    }

    // Engage / disengage the razor as the router's selected-mode tool. Setting the cursor here gives
    // an immediate affordance on toggle; the razor tool's Hover refines it per hit-zone as the
    // pointer moves (crosshair over a clip, arrow over empty lane).
    private void ApplyRazorMode(bool on)
    {
        _toolRouter.SelectedModeTool = on ? _razorTool : null;
        Cursor = on ? Cursors.Cross : null;
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

    // The marquee band lives on LanesHost's adorner layer (the MainScroller's ScrollContentPresenter
    // layer — same one ClipsLaneView's drag-ghost uses), so it spans every lane and is positioned in
    // LanesHost content space, matching the rect the MarqueeTool builds from captured content points.
    void ITimelineSurface.ShowMarquee(Rect contentRect)
    {
        var layer = AdornerLayer.GetAdornerLayer(LanesHost);
        if (layer is null) return;
        if (_marqueeAdorner is null)
        {
            _marqueeAdorner = new MarqueeAdorner(LanesHost);
            layer.Add(_marqueeAdorner);
        }
        _marqueeAdorner.UpdateRect(contentRect);
    }

    void ITimelineSurface.HideMarquee()
    {
        if (_marqueeAdorner is null) return;
        var layer = AdornerLayer.GetAdornerLayer(LanesHost);
        layer?.Remove(_marqueeAdorner);
        _marqueeAdorner = null;
    }
}

/// <summary>
/// Translucent rubber-band rectangle painted on the lanes container's <see cref="AdornerLayer"/>
/// during a marquee multi-select drag (#12 phase 6). Lives on the timeline-wide <c>LanesHost</c>
/// (not a single lane) so the band spans tracks; its rect is in <c>LanesHost</c> content space, the
/// same space <see cref="MarqueeTool"/> builds the drag rect in. Hit-test-transparent so it never
/// intercepts the gesture. Mirrors <c>GhostClipAdorner</c>'s shape.
/// </summary>
internal sealed class MarqueeAdorner : Adorner
{
    private static readonly Brush Fill = CreateFill();
    private static readonly Pen Border = CreateBorder();
    private Rect _rect;

    public MarqueeAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    public void UpdateRect(Rect rect)
    {
        _rect = rect;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_rect.Width <= 0 || _rect.Height <= 0) return;
        dc.DrawRectangle(Fill, Border, _rect);
    }

    private static Brush CreateFill()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x33, 0x4D, 0x9D, 0xE0));
        brush.Freeze();
        return brush;
    }

    private static Pen CreateBorder()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x4D, 0x9D, 0xE0)), 1.0);
        pen.Freeze();
        return pen;
    }
}
