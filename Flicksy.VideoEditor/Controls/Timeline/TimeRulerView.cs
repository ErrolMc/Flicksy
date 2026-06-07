using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Controls.Timeline;

/// <summary>
/// Top ruler for the timeline. <c>DataContext</c> is the host's <see cref="TimelineViewModel"/>.
/// Renders second-aligned tick marks + labels along its width; the tick step is chosen so
/// labels stay roughly <see cref="MinLabelSpacingPx"/> pixels apart regardless of the
/// current <see cref="TimelineViewModel.PixelsPerFrame"/>. Width is measured to cover the
/// snapshotted <see cref="TransportViewModel.TotalFrames"/> at the current zoom, so the
/// hosting horizontal ScrollViewer reports the right scrollable extent.
/// </summary>
public sealed class TimeRulerView : FrameworkElement
{
    private const double RulerHeight = 24;
    private const double MinLabelSpacingPx = 70;
    private const double MinContentWidth = 200;

    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
    private static readonly Brush TickBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));
    private static readonly Brush MinorTickBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD));
    private static readonly Brush BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly Pen BorderPen = MakeFrozenPen(BorderBrush, 1);
    private static readonly Pen TickPen = MakeFrozenPen(TickBrush, 1);
    private static readonly Pen MinorTickPen = MakeFrozenPen(MinorTickBrush, 1);
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private static readonly double[] StepSecondsCandidates =
    {
        // Sub-second: 1, 2, 5 frames at 30fps map to ~0.033, 0.067, 0.167s — fall back to
        // 1s minimum for label readability.
        1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600,
    };

    private TimelineViewModel? _viewModel;
    private TransportViewModel? _transport;
    private PropertyChangedEventHandler? _vmHandler;
    private PropertyChangedEventHandler? _transportHandler;

    public TimeRulerView()
    {
        DataContextChanged += OnDataContextChanged;
        ClipToBounds = true;
        Cursor = Cursors.IBeam;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_viewModel is null) 
            return;
        CaptureMouse();
        SeekFromMouse(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || _viewModel is null) 
            return;
        SeekFromMouse(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) 
            ReleaseMouseCapture();
    }

    private void SeekFromMouse(MouseEventArgs e)
    {
        if (_viewModel is null) 
            return;
        _viewModel.SeekToPixel(e.GetPosition(this).X);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null && _vmHandler is not null)
        {
            _viewModel.PropertyChanged -= _vmHandler;
        }
        if (_transport is not null && _transportHandler is not null)
        {
            _transport.PropertyChanged -= _transportHandler;
        }

        _viewModel = e.NewValue as TimelineViewModel;
        _transport = _viewModel?.Transport;
        if (_viewModel is not null)
        {
            _vmHandler = OnViewModelPropertyChanged;
            _viewModel.PropertyChanged += _vmHandler;
        }
        if (_transport is not null)
        {
            _transportHandler = OnTransportPropertyChanged;
            _transport.PropertyChanged += _transportHandler;
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.PixelsPerFrame))
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private void OnTransportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // TotalFrames drives MeasureContentWidth — clip add/remove on any track flips it
        // through TransportViewModel's project-mutation subscription. Without this the
        // ruler stays at its construction-time width and clicks past the original extent
        // can't move the playhead.
        if (e.PropertyName == nameof(TransportViewModel.TotalFrames))
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = MeasureContentWidth();
        return new Size(width, RulerHeight);
    }

    private double MeasureContentWidth()
    {
        if (_viewModel is null) 
            return MinContentWidth;

        int totalFrames = Math.Max(_viewModel.Transport.TotalFrames, 1);
        return Math.Max(MinContentWidth, totalFrames * _viewModel.PixelsPerFrame);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0) 
            return;

        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));

        if (_viewModel is null)
        {
            DrawBottomBorder(dc, width, height);
            return;
        }

        int framerate = Math.Max(1, _viewModel.Project.Settings.Framerate);
        double pxPerFrame = _viewModel.PixelsPerFrame;
        double pxPerSecond = framerate * pxPerFrame;
        if (pxPerSecond <= 0)
        {
            DrawBottomBorder(dc, width, height);
            return;
        }

        double stepSeconds = ChooseStepSeconds(pxPerSecond);
        double pxPerStep = stepSeconds * pxPerSecond;

        // Minor ticks: 4 subdivisions per labeled step where they're visually distinct
        // (>=8px apart). Keeps the ruler legible even when zoomed in tight.
        double minorPxPerTick = pxPerStep / 4.0;
        if (minorPxPerTick >= 8)
        {
            for (double x = minorPxPerTick; x < width; x += minorPxPerTick)
            {
                double px = SnapToPixel(x);
                dc.DrawLine(MinorTickPen, new Point(px, height - 4), new Point(px, height));
            }
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (double x = 0; x < width; x += pxPerStep)
        {
            double px = SnapToPixel(x);
            dc.DrawLine(TickPen, new Point(px, height - 8), new Point(px, height));

            double seconds = x / pxPerSecond;
            string label = FormatTimecode(seconds);
            var ft = new FormattedText(
                label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                10,
                LabelBrush,
                pixelsPerDip);
            dc.DrawText(ft, new Point(px + 3, 2));
        }

        DrawBottomBorder(dc, width, height);
    }

    private static void DrawBottomBorder(DrawingContext dc, double width, double height)
    {
        double y = SnapToPixel(height - 0.5);
        dc.DrawLine(BorderPen, new Point(0, y), new Point(width, y));
    }

    private static double SnapToPixel(double value) => Math.Round(value) + 0.5;

    private static double ChooseStepSeconds(double pxPerSecond)
    {
        foreach (double s in StepSecondsCandidates)
        {
            if (s * pxPerSecond >= MinLabelSpacingPx) 
                return s;
        }
        return StepSecondsCandidates[^1];
    }

    private static string FormatTimecode(double seconds)
    {
        int totalSeconds = (int)Math.Round(seconds);
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds / 60) % 60;
        int secs = totalSeconds % 60;
        return hours > 0
            ? $"{hours:D1}:{minutes:D2}:{secs:D2}"
            : $"{minutes:D2}:{secs:D2}";
    }

    private static Pen MakeFrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        if (pen.CanFreeze) 
            pen.Freeze();
        return pen;
    }
}
