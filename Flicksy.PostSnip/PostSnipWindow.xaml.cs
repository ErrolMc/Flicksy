using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Flicksy.PostSnip.ViewModels;
using Microsoft.Win32;

namespace Flicksy.PostSnip;

public partial class PostSnipWindow : Window
{
    // Windows 10 1809 used attribute 19; 1903+ uses 20. Try the newer one first
    // and fall back to the older one — DwmSetWindowAttribute returns non-zero on
    // unsupported attributes, which is harmless to ignore.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    private const int WM_MOUSEHWHEEL = 0x020E;
    private const double ZoomStep = 1.15;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 32.0;
    private const double ScrollPixelsPerWheelNotch = 60.0;
    private const double ViewportEdgeMargin = 50.0;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private bool _isPanning;
    private Point _lastPanPoint;
    private Cursor? _cursorBeforePan;
    private bool _autoFitPending = true;

    public PostSnipWindow(PostSnipViewModel viewModel)
    {
        InitializeComponent();

        ViewModel = viewModel;
        DataContext = viewModel;

        viewModel.SaveDialogRequested += OnSaveDialogRequested;
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.ErrorOccurred += OnErrorOccurred;
        viewModel.CropOverlay.ViewBoundsChanged += OnCropViewBoundsChanged;

        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
        {
            return;
        }

        if (!ViewModel.SelectionOverlay.IsVisible)
        {
            return;
        }

        if (ViewModel.Drawing.DeleteSelectedItem())
        {
            e.Handled = true;
        }
    }

    public PostSnipViewModel ViewModel { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) 
            return;

        int useDark = 1;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, sizeof(int));
        }

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOUSEHWHEEL) 
            return IntPtr.Zero;

        short delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        short screenX = (short)(lParam.ToInt64() & 0xFFFF);
        short screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        if (ImageViewport.Visibility != Visibility.Visible) 
            return IntPtr.Zero;

        Point local;
        try
        {
            local = ImageViewport.PointFromScreen(new Point(screenX, screenY));
        }
        catch
        {
            return IntPtr.Zero;
        }

        if (local.X < 0 || local.Y < 0 ||
            local.X > ImageViewport.ActualWidth ||
            local.Y > ImageViewport.ActualHeight)
        {
            return IntPtr.Zero;
        }

        double pixels = (delta / 120.0) * ScrollPixelsPerWheelNotch;
        PanBy(-pixels, 0);
        handled = true;
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.SaveDialogRequested -= OnSaveDialogRequested;
        ViewModel.CloseRequested -= OnCloseRequested;
        ViewModel.ErrorOccurred -= OnErrorOccurred;
        ViewModel.CropOverlay.ViewBoundsChanged -= OnCropViewBoundsChanged;

        ViewModel.Player.Close();
        ViewModel.Player.Dispose();
        ViewModel.DeleteMediaFile();

        base.OnClosed(e);
    }

    private void OnCropViewBoundsChanged(object? sender, EventArgs e)
    {
        UpdateImageClip();
        _autoFitPending = true;
        TryAutoFit();
    }

    private void UpdateImageClip()
    {
        CropOverlayViewModel crop = ViewModel.CropOverlay;
        if (!crop.HasImage || crop.IsActive || !crop.IsCropped)
        {
            ImageContent.Clip = null;
            return;
        }

        ImageContent.Clip = new RectangleGeometry(crop.CommittedCrop);
    }

    private void OnSaveDialogRequested(object? sender, SaveDialogRequest request)
    {
        var dialog = new SaveFileDialog
        {
            Title = request.Title,
            FileName = request.SuggestedFileName,
            DefaultExt = request.DefaultExtension,
            AddExtension = true,
            OverwritePrompt = true,
            Filter = request.Filter,
        };

        if (dialog.ShowDialog(this) == true)
        {
            request.SelectedPath = dialog.FileName;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnErrorOccurred(object? sender, string message)
    {
        MessageBox.Show(this, message, "Flicksy", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnImageViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_autoFitPending)
        {
            TryAutoFit();
        }
        else
        {
            ClampOffsets();
        }
    }

    private void OnEditedImageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _autoFitPending = true;
        TryAutoFit();
    }

    private void TryAutoFit()
    {
        double imgW = EditedImage.ActualWidth;
        double imgH = EditedImage.ActualHeight;
        double portW = ImageViewport.ActualWidth;
        double portH = ImageViewport.ActualHeight;
        if (imgW <= 0 || imgH <= 0 || portW <= 0 || portH <= 0) 
            return;

        Rect bounds = GetVisibleBounds(imgW, imgH);

        double scale = Math.Min(portW / bounds.Width, portH / bounds.Height);

        ImageScaleTransform.ScaleX = scale;
        ImageScaleTransform.ScaleY = scale;
        // Centre the visible region (cropped or full) in the viewport.
        double centerX = bounds.X + bounds.Width / 2.0;
        double centerY = bounds.Y + bounds.Height / 2.0;
        ImageTranslateTransform.X = portW / 2.0 - centerX * scale;
        ImageTranslateTransform.Y = portH / 2.0 - centerY * scale;
        _autoFitPending = false;

        UpdateImageClip();
    }

    private Rect GetVisibleBounds(double imgW, double imgH)
    {
        CropOverlayViewModel? crop = ViewModel?.CropOverlay;
        var fallback = new Rect(0, 0, imgW, imgH);
        if (crop is null || !crop.HasImage)
        {
            return fallback;
        }

        Rect bounds = crop.CurrentViewBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return fallback;
        }

        return bounds;
    }

    private void OnImageViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            double factor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
            ZoomAt(e.GetPosition(ImageViewport), factor);
        }
        else
        {
            double pixels = (e.Delta / 120.0) * ScrollPixelsPerWheelNotch;
            PanBy(0, pixels);
        }
        e.Handled = true;
    }

    private void ZoomAt(Point viewportPoint, double factor)
    {
        double oldScale = ImageScaleTransform.ScaleX;
        double newScale = Math.Clamp(oldScale * factor, MinZoom, MaxZoom);
        if (Math.Abs(newScale - oldScale) < 1e-9) 
            return;

        double imageX = (viewportPoint.X - ImageTranslateTransform.X) / oldScale;
        double imageY = (viewportPoint.Y - ImageTranslateTransform.Y) / oldScale;

        ImageScaleTransform.ScaleX = newScale;
        ImageScaleTransform.ScaleY = newScale;
        ImageTranslateTransform.X = viewportPoint.X - imageX * newScale;
        ImageTranslateTransform.Y = viewportPoint.Y - imageY * newScale;
        ClampOffsets();
    }

    private void PanBy(double dx, double dy)
    {
        ImageTranslateTransform.X += dx;
        ImageTranslateTransform.Y += dy;
        ClampOffsets();
    }

    private void ClampOffsets()
    {
        double portW = ImageViewport.ActualWidth;
        double portH = ImageViewport.ActualHeight;
        double rawImgW = EditedImage.ActualWidth;
        double rawImgH = EditedImage.ActualHeight;
        if (portW <= 0 || portH <= 0 || rawImgW <= 0 || rawImgH <= 0) 
            return;

        double scale = ImageScaleTransform.ScaleX;
        Rect bounds = GetVisibleBounds(rawImgW, rawImgH);

        // Clamp so that at least ViewportEdgeMargin pixels of the visible region stay on-screen.
        double minX = ViewportEdgeMargin - bounds.Right * scale;
        double maxX = portW - ViewportEdgeMargin - bounds.X * scale;
        if (minX > maxX)
        {
            double mid = (minX + maxX) / 2.0;
            minX = maxX = mid;
        }
        double minY = ViewportEdgeMargin - bounds.Bottom * scale;
        double maxY = portH - ViewportEdgeMargin - bounds.Y * scale;
        if (minY > maxY)
        {
            double mid = (minY + maxY) / 2.0;
            minY = maxY = mid;
        }

        ImageTranslateTransform.X = Math.Clamp(ImageTranslateTransform.X, minX, maxX);
        ImageTranslateTransform.Y = Math.Clamp(ImageTranslateTransform.Y, minY, maxY);
    }

    private void OnImageViewportPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) 
            return;

        _isPanning = true;
        _lastPanPoint = e.GetPosition(ImageViewport);
        _cursorBeforePan = ImageViewport.Cursor;
        ImageViewport.Cursor = Cursors.Hand;
        ImageViewport.CaptureMouse();
        e.Handled = true;
    }

    private void OnImageViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) 
            return;

        Point current = e.GetPosition(ImageViewport);
        PanBy(current.X - _lastPanPoint.X, current.Y - _lastPanPoint.Y);
        _lastPanPoint = current;
    }

    private void OnImageViewportPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning || e.ChangedButton != MouseButton.Middle) 
            return;

        _isPanning = false;
        ImageViewport.ReleaseMouseCapture();
        ImageViewport.Cursor = _cursorBeforePan;
        _cursorBeforePan = null;
        e.Handled = true;
    }
}
