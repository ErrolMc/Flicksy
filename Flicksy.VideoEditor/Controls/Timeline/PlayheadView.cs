using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Controls.Timeline;

/// <summary>
/// Vertical playhead overlay. <c>DataContext</c> is the host's <see cref="TimelineViewModel"/>.
/// Draws a single line at <see cref="TransportViewModel.Playhead"/> × <see cref="TimelineViewModel.PixelsPerFrame"/>
/// across the full height of its slot. Hit-test-transparent so clicks below (clip
/// selection, lane drags) still reach their targets.
/// </summary>
public sealed class PlayheadView : FrameworkElement
{
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0x55, 0x55));
    private static readonly Pen LinePen = MakeFrozenPen(LineBrush, 1);

    private TimelineViewModel? _viewModel;
    private TransportViewModel? _transport;
    private PropertyChangedEventHandler? _vmHandler;
    private PropertyChangedEventHandler? _transportHandler;

    public PlayheadView()
    {
        IsHitTestVisible = false;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();

        _viewModel = e.NewValue as TimelineViewModel;
        if (_viewModel is not null)
        {
            _vmHandler = OnViewModelPropertyChanged;
            _viewModel.PropertyChanged += _vmHandler;

            _transport = _viewModel.Transport;
            _transportHandler = OnTransportPropertyChanged;
            _transport.PropertyChanged += _transportHandler;
        }

        InvalidateVisual();
    }

    private void Detach()
    {
        if (_viewModel is not null && _vmHandler is not null)
        {
            _viewModel.PropertyChanged -= _vmHandler;
        }
        if (_transport is not null && _transportHandler is not null)
        {
            _transport.PropertyChanged -= _transportHandler;
        }
        _viewModel = null;
        _transport = null;
        _vmHandler = null;
        _transportHandler = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.PixelsPerFrame))
        {
            InvalidateVisual();
        }
    }

    private void OnTransportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransportViewModel.Playhead))
        {
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_viewModel is null) return;

        double height = ActualHeight;
        if (height <= 0) 
            return;

        double x = _viewModel.Transport.Playhead * _viewModel.PixelsPerFrame;
        double snapped = SnapToPixel(x);
        dc.DrawLine(LinePen, new Point(snapped, 0), new Point(snapped, height));
    }

    private static double SnapToPixel(double value) => System.Math.Round(value) + 0.5;

    private static Pen MakeFrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        if (pen.CanFreeze) 
            pen.Freeze();
        return pen;
    }
}
