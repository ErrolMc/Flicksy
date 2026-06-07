using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flicksy.Drawing.Media;

namespace Flicksy.Drawing.Controls;

public partial class VideoSurface : UserControl
{
    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register(
            nameof(Player),
            typeof(IVideoPlayer),
            typeof(VideoSurface),
            new PropertyMetadata(null, OnPlayerChanged));

    private WriteableBitmap? _bitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;

    public VideoSurface()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    public IVideoPlayer? Player
    {
        get => (IVideoPlayer?)GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        VideoSurface surface = (VideoSurface)d;

        if (e.OldValue is IVideoPlayer oldPlayer)
        {
            oldPlayer.FrameReady -= surface.OnFrameReady;
        }

        if (e.NewValue is IVideoPlayer newPlayer)
        {
            newPlayer.FrameReady += surface.OnFrameReady;
        }
    }

    private void OnFrameReady(object? sender, VideoFrame frame)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => CopyFrameToBitmap(frame));
            return;
        }

        CopyFrameToBitmap(frame);
    }

    private void CopyFrameToBitmap(VideoFrame frame)
    {
        EnsureBitmap(frame.Width, frame.Height);
        if (_bitmap is null) return;

        var rect = new Int32Rect(0, 0, frame.Width, frame.Height);
        _bitmap.WritePixels(rect, frame.Buffer, frame.Stride, 0);
    }

    private void EnsureBitmap(int width, int height)
    {
        if (_bitmap is not null && _bitmapWidth == width && _bitmapHeight == height)
        {
            return;
        }

        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        _bitmapWidth = width;
        _bitmapHeight = height;
        FrameImage.Source = _bitmap;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (Player is { } p)
        {
            p.FrameReady -= OnFrameReady;
        }
    }
}
