using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfPoint = System.Windows.Point;

namespace Flicksy.Snipper.Overlays;

public partial class PreSnipOverlayWindow : Window
{
    private static readonly SolidColorBrush SelectedModeButtonBackgroundBrush = new(System.Windows.Media.Color.FromRgb(0, 120, 212));
    private static readonly SolidColorBrush UnselectedModeButtonBackgroundBrush = new(System.Windows.Media.Color.FromRgb(51, 51, 51));
    private static readonly SolidColorBrush SelectedModeButtonBorderBrush = new(System.Windows.Media.Color.FromRgb(0, 120, 212));
    private static readonly SolidColorBrush UnselectedModeButtonBorderBrush = new(System.Windows.Media.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));

    private readonly DrawingRectangle _screenBounds;
    private readonly Action<Bitmap> _onSnipCaptured;
    private readonly Action<DrawingRectangle, DrawingRectangle> _onVideoAreaSelected;
    private readonly Bitmap _backgroundBitmap;
    private bool _isSelecting;
    private bool _hasSelection;
    private bool _isSnipMode = true;
    private WpfPoint _selectionStart;
    private Rect _selectionRect;

    public PreSnipOverlayWindow(
        DrawingRectangle bounds,
        Action<Bitmap> onSnipCaptured,
        Action<DrawingRectangle, DrawingRectangle> onVideoAreaSelected)
    {
        _screenBounds = bounds;
        _onSnipCaptured = onSnipCaptured;
        _onVideoAreaSelected = onVideoAreaSelected;

        InitializeComponent();

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        _backgroundBitmap = new Bitmap(bounds.Width, bounds.Height);
        using (Graphics graphics = Graphics.FromImage(_backgroundBitmap))
        {
            graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
        }

        BackgroundImage.Source = CreateBitmapSource(_backgroundBitmap);
        SetMode(true);
        Loaded += (_, _) =>
        {
            Focus();
            UpdateSelectionVisuals();
        };
        SizeChanged += (_, _) => UpdateSelectionVisuals();
    }

    protected override void OnClosed(EventArgs e)
    {
        _backgroundBitmap.Dispose();
        base.OnClosed(e);
    }

    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void OnSnipModeClick(object sender, RoutedEventArgs e)
    {
        SetMode(true);
    }

    private void OnRecordModeClick(object sender, RoutedEventArgs e)
    {
        SetMode(false);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideMenu(e.OriginalSource))
        {
            return;
        }

        _isSelecting = true;
        _hasSelection = true;
        _selectionStart = ClampToCanvas(e.GetPosition(OverlayCanvas));
        _selectionRect = Rect.Empty;
        Mouse.Capture(this);
        UpdateSelectionVisuals();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        WpfPoint current = ClampToCanvas(e.GetPosition(OverlayCanvas));
        Rect rawSelection = CreateRect(_selectionStart, current);
        _selectionRect = _isSnipMode
            ? rawSelection
            : NormalizeRecordingSelectionRect(rawSelection);
        UpdateSelectionVisuals();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        Mouse.Capture(null);
        UpdateSelectionVisuals();

        DrawingRectangle selection = GetSelectionRectangle();
        if (selection.Width < 2 || selection.Height < 2)
        {
            return;
        }

        if (_isSnipMode)
        {
            var captured = new Bitmap(selection.Width, selection.Height);
            using (Graphics graphics = Graphics.FromImage(captured))
            {
                graphics.DrawImage(
                    _backgroundBitmap,
                    new DrawingRectangle(0, 0, selection.Width, selection.Height),
                    selection,
                    GraphicsUnit.Pixel);
            }

            _onSnipCaptured(captured);
            Close();
            return;
        }

        _onVideoAreaSelected(_screenBounds, selection);
        Close();
    }

    private void SetMode(bool isSnipMode)
    {
        _isSnipMode = isSnipMode;

        SetButtonStyle(SnipModeButton, isSnipMode);
        SetButtonStyle(RecordModeButton, !isSnipMode);

        SelectionBorder.Stroke = isSnipMode ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Red;
        InstructionText.Text = isSnipMode
            ? "Click and drag an area to snip"
            : "Click and drag an area to record";
    }

    private static void SetButtonStyle(System.Windows.Controls.Button button, bool selected)
    {
        button.Background = selected
            ? SelectedModeButtonBackgroundBrush
            : UnselectedModeButtonBackgroundBrush;
        button.BorderBrush = selected
            ? SelectedModeButtonBorderBrush
            : UnselectedModeButtonBorderBrush;
        button.BorderThickness = new Thickness(1);
    }

    private void UpdateSelectionVisuals()
    {
        double canvasWidth = OverlayCanvas.ActualWidth;
        double canvasHeight = OverlayCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return;
        }

        if (!_hasSelection || _selectionRect.IsEmpty || _selectionRect.Width <= 0 || _selectionRect.Height <= 0)
        {
            SetCanvasRect(TopShade, 0, 0, canvasWidth, canvasHeight);
            SetCanvasRect(LeftShade, 0, 0, 0, 0);
            SetCanvasRect(RightShade, 0, 0, 0, 0);
            SetCanvasRect(BottomShade, 0, 0, 0, 0);
            SelectionBorder.Visibility = Visibility.Collapsed;
            return;
        }

        Rect rect = _selectionRect;
        SetCanvasRect(TopShade, 0, 0, canvasWidth, rect.Y);
        SetCanvasRect(LeftShade, 0, rect.Y, rect.X, rect.Height);
        SetCanvasRect(RightShade, rect.Right, rect.Y, canvasWidth - rect.Right, rect.Height);
        SetCanvasRect(BottomShade, 0, rect.Bottom, canvasWidth, canvasHeight - rect.Bottom);

        SetCanvasRect(SelectionBorder, rect.X, rect.Y, rect.Width, rect.Height);
        SelectionBorder.Visibility = Visibility.Visible;
    }

    private DrawingRectangle GetSelectionRectangle()
    {
        return GetClampedSelectionRectangle(useCeilingForSize: _isSnipMode, requireEvenSize: !_isSnipMode);
    }

    private DrawingRectangle GetClampedSelectionRectangle(bool useCeilingForSize, bool requireEvenSize)
    {
        int maxWidth = _backgroundBitmap.Width;
        int maxHeight = _backgroundBitmap.Height;
        int x = Math.Clamp((int)Math.Floor(_selectionRect.X), 0, Math.Max(maxWidth - 1, 0));
        int y = Math.Clamp((int)Math.Floor(_selectionRect.Y), 0, Math.Max(maxHeight - 1, 0));

        int rawWidth = useCeilingForSize
            ? (int)Math.Ceiling(_selectionRect.Width)
            : (int)Math.Floor(_selectionRect.Width);
        int rawHeight = useCeilingForSize
            ? (int)Math.Ceiling(_selectionRect.Height)
            : (int)Math.Floor(_selectionRect.Height);

        int width = Math.Clamp(rawWidth, 0, maxWidth - x);
        int height = Math.Clamp(rawHeight, 0, maxHeight - y);

        if (requireEvenSize)
        {
            width &= ~1;
            height &= ~1;
        }

        return new DrawingRectangle(x, y, width, height);
    }

    private static Rect NormalizeRecordingSelectionRect(Rect rect)
    {
        double x = Math.Floor(rect.X);
        double y = Math.Floor(rect.Y);
        double width = Math.Floor(rect.Width);
        double height = Math.Floor(rect.Height);

        width = Math.Max(0, width - (width % 2));
        height = Math.Max(0, height - (height % 2));

        return new Rect(x, y, width, height);
    }

    private static bool IsInsideMenu(object? source)
    {
        if (source is not DependencyObject dependencyObject)
        {
            return false;
        }

        DependencyObject current = dependencyObject;
        while (current is not null)
        {
            if (current is Border border && border.Name == "MenuBorder")
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private WpfPoint ClampToCanvas(WpfPoint point)
    {
        double x = Math.Clamp(point.X, 0, OverlayCanvas.ActualWidth);
        double y = Math.Clamp(point.Y, 0, OverlayCanvas.ActualHeight);
        return new WpfPoint(x, y);
    }

    private static Rect CreateRect(WpfPoint a, WpfPoint b)
    {
        double left = Math.Min(a.X, b.X);
        double top = Math.Min(a.Y, b.Y);
        double width = Math.Abs(b.X - a.X);
        double height = Math.Abs(b.Y - a.Y);
        return new Rect(left, top, width, height);
    }

    private static void SetCanvasRect(FrameworkElement element, double x, double y, double width, double height)
    {
        Canvas.SetLeft(element, Math.Max(0, x));
        Canvas.SetTop(element, Math.Max(0, y));
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private static BitmapSource CreateBitmapSource(Bitmap bitmap)
    {
        nint hBitmap = bitmap.GetHbitmap();
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
