using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Flicksy.PostSnip.ViewModels;

namespace Flicksy.PostSnip.Controls;

public partial class CropOverlayView : UserControl
{
    // Visual constants (viewport pixels).
    private const double CornerHitSize = 24.0;
    private const double EdgeHitThickness = 12.0;
    private const double BracketArmLength = 14.0;
    private const double BracketThickness = 3.0;
    private const double EdgeMarkerLength = 18.0;
    private const double EdgeMarkerThickness = 3.0;

    private enum GestureKind
    {
        None,
        MoveRect,
        DrawNew,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight,
        ResizeTop,
        ResizeBottom,
        ResizeLeft,
        ResizeRight,
    }

    public static readonly DependencyProperty ContentToViewportProperty =
        DependencyProperty.Register(
            nameof(ContentToViewport),
            typeof(Transform),
            typeof(CropOverlayView),
            new PropertyMetadata(null, OnContentToViewportChanged));

    private GestureKind _gesture = GestureKind.None;
    private Point _gestureAnchorImage;
    private Point _gestureStartImage;
    private Rect _gestureStartCrop;

    public CropOverlayView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => UpdateLayoutFromState();
        SizeChanged += (_, _) => UpdateLayoutFromState();
    }

    public Transform? ContentToViewport
    {
        get => (Transform?)GetValue(ContentToViewportProperty);
        set => SetValue(ContentToViewportProperty, value);
    }

    private CropOverlayViewModel? ViewModel => DataContext as CropOverlayViewModel;

    private static void OnContentToViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (CropOverlayView)d;
        if (e.OldValue is Transform oldTransform)
        {
            oldTransform.Changed -= view.OnTransformChanged;
        }

        if (e.NewValue is Transform newTransform)
        {
            newTransform.Changed += view.OnTransformChanged;
        }

        view.UpdateLayoutFromState();
    }

    private void OnTransformChanged(object? sender, EventArgs e)
    {
        UpdateLayoutFromState();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CropOverlayViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is CropOverlayViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateLayoutFromState();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CropOverlayViewModel.EffectiveCrop)
            or nameof(CropOverlayViewModel.WorkingCrop)
            or nameof(CropOverlayViewModel.CommittedCrop)
            or nameof(CropOverlayViewModel.IsActive)
            or nameof(CropOverlayViewModel.ImageWidth)
            or nameof(CropOverlayViewModel.ImageHeight))
        {
            UpdateLayoutFromState();
        }
    }

    private void UpdateLayoutFromState()
    {
        CropOverlayViewModel? vm = ViewModel;
        if (vm is null || !vm.IsActive || !vm.HasImage)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;

        Rect crop = vm.EffectiveCrop;
        Rect imageRect = vm.ImageBounds;
        Transform? transform = ContentToViewport;

        Rect cropVp = TransformRect(transform, crop);
        Rect imageVp = TransformRect(transform, imageRect);

        // Dim regions: cover the image area outside the crop rect.
        double leftWidth = Math.Max(0, cropVp.Left - imageVp.Left);
        double rightX = cropVp.Right;
        double rightWidth = Math.Max(0, imageVp.Right - cropVp.Right);
        double topHeight = Math.Max(0, cropVp.Top - imageVp.Top);
        double bottomY = cropVp.Bottom;
        double bottomHeight = Math.Max(0, imageVp.Bottom - cropVp.Bottom);

        SetCanvasRect(TopShade, imageVp.Left, imageVp.Top, imageVp.Width, topHeight);
        SetCanvasRect(BottomShade, imageVp.Left, bottomY, imageVp.Width, bottomHeight);
        SetCanvasRect(LeftShade, imageVp.Left, cropVp.Top, leftWidth, cropVp.Height);
        SetCanvasRect(RightShade, rightX, cropVp.Top, rightWidth, cropVp.Height);

        // Outline.
        SetCanvasRect(CropOutline, cropVp.Left, cropVp.Top, cropVp.Width, cropVp.Height);

        // Corner brackets: L-shapes positioned just inside each corner so the
        // stroke straddles the crop edge.
        double l = cropVp.Left;
        double t = cropVp.Top;
        double r = cropVp.Right;
        double b = cropVp.Bottom;
        double arm = Math.Min(BracketArmLength, Math.Min(cropVp.Width, cropVp.Height) / 2.0);

        SetBracketPoints(TopLeftBracket, new Point(l, t + arm), new Point(l, t), new Point(l + arm, t));
        SetBracketPoints(TopRightBracket, new Point(r - arm, t), new Point(r, t), new Point(r, t + arm));
        SetBracketPoints(BottomLeftBracket, new Point(l, b - arm), new Point(l, b), new Point(l + arm, b));
        SetBracketPoints(BottomRightBracket, new Point(r - arm, b), new Point(r, b), new Point(r, b - arm));

        // Edge midpoint markers — short white bars centered on each edge midpoint.
        double midX = (l + r) / 2.0;
        double midY = (t + b) / 2.0;
        double markerLen = Math.Min(EdgeMarkerLength, Math.Min(cropVp.Width, cropVp.Height) - 2 * arm);
        if (markerLen < 4) 
            markerLen = 0;

        if (markerLen > 0)
        {
            SetCanvasRect(TopEdgeMarker, midX - markerLen / 2.0, t - EdgeMarkerThickness / 2.0, markerLen, EdgeMarkerThickness);
            SetCanvasRect(BottomEdgeMarker, midX - markerLen / 2.0, b - EdgeMarkerThickness / 2.0, markerLen, EdgeMarkerThickness);
            SetCanvasRect(LeftEdgeMarker, l - EdgeMarkerThickness / 2.0, midY - markerLen / 2.0, EdgeMarkerThickness, markerLen);
            SetCanvasRect(RightEdgeMarker, r - EdgeMarkerThickness / 2.0, midY - markerLen / 2.0, EdgeMarkerThickness, markerLen);
            TopEdgeMarker.Visibility = Visibility.Visible;
            BottomEdgeMarker.Visibility = Visibility.Visible;
            LeftEdgeMarker.Visibility = Visibility.Visible;
            RightEdgeMarker.Visibility = Visibility.Visible;
        }
        else
        {
            TopEdgeMarker.Visibility = Visibility.Collapsed;
            BottomEdgeMarker.Visibility = Visibility.Collapsed;
            LeftEdgeMarker.Visibility = Visibility.Collapsed;
            RightEdgeMarker.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        CropOverlayViewModel? vm = ViewModel;
        if (vm is null || !vm.IsActive || !vm.HasImage)
        {
            return;
        }

        Point viewportPoint = e.GetPosition(OverlayCanvas);
        Point imagePoint = ToImagePoint(viewportPoint);
        Rect imageRect = vm.ImageBounds;
        if (!ContainsInclusive(imageRect, imagePoint))
        {
            return;
        }

        Rect crop = vm.EffectiveCrop;
        Rect cropVp = TransformRect(ContentToViewport, crop);

        GestureKind gesture = DetermineGesture(viewportPoint, cropVp);
        if (gesture == GestureKind.None)
        {
            return;
        }

        _gesture = gesture;
        _gestureStartCrop = crop;
        _gestureStartImage = imagePoint;
        _gestureAnchorImage = ComputeAnchorImage(gesture, crop);

        if (gesture == GestureKind.DrawNew)
        {
            vm.SetWorkingCrop(new Rect(imagePoint, new Size(0, 0)));
        }

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        CropOverlayViewModel? vm = ViewModel;
        if (vm is null || !vm.IsActive || !vm.HasImage)
        {
            return;
        }

        if (_gesture == GestureKind.None)
        {
            UpdateCursor(e.GetPosition(OverlayCanvas), vm);
            return;
        }

        Point imagePoint = ClampToImage(ToImagePoint(e.GetPosition(OverlayCanvas)), vm);

        Rect newCrop = _gesture switch
        {
            GestureKind.MoveRect => MoveCrop(_gestureStartCrop, imagePoint - _gestureStartImage, vm),
            GestureKind.DrawNew => RectFromPoints(_gestureStartImage, imagePoint),
            _ => ResizeCrop(_gesture, _gestureStartCrop, _gestureAnchorImage, imagePoint),
        };

        vm.SetWorkingCrop(newCrop);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_gesture == GestureKind.None)
        {
            return;
        }

        _gesture = GestureKind.None;
        ReleaseMouseCapture();
        e.Handled = true;

        if (ViewModel is { } vm)
        {
            UpdateCursor(e.GetPosition(OverlayCanvas), vm);
        }
    }

    private void UpdateCursor(Point viewportPoint, CropOverlayViewModel vm)
    {
        Rect cropVp = TransformRect(ContentToViewport, vm.EffectiveCrop);
        Cursor = DetermineGesture(viewportPoint, cropVp) switch
        {
            GestureKind.MoveRect => Cursors.SizeAll,
            GestureKind.ResizeTopLeft or GestureKind.ResizeBottomRight => Cursors.SizeNWSE,
            GestureKind.ResizeTopRight or GestureKind.ResizeBottomLeft => Cursors.SizeNESW,
            GestureKind.ResizeTop or GestureKind.ResizeBottom => Cursors.SizeNS,
            GestureKind.ResizeLeft or GestureKind.ResizeRight => Cursors.SizeWE,
            GestureKind.DrawNew => Cursors.Cross,
            _ => Cursors.Arrow,
        };
    }

    private static GestureKind DetermineGesture(Point viewportPoint, Rect cropVp)
    {
        if (cropVp.Width <= 0 || cropVp.Height <= 0)
        {
            return GestureKind.DrawNew;
        }

        double halfCorner = CornerHitSize / 2.0;
        double halfEdge = EdgeHitThickness / 2.0;

        // Corner hit boxes (centered on each corner point).
        if (InBox(viewportPoint, cropVp.Left, cropVp.Top, halfCorner)) 
            return GestureKind.ResizeTopLeft;

        if (InBox(viewportPoint, cropVp.Right, cropVp.Top, halfCorner)) 
            return GestureKind.ResizeTopRight;

        if (InBox(viewportPoint, cropVp.Left, cropVp.Bottom, halfCorner)) 
            return GestureKind.ResizeBottomLeft;

        if (InBox(viewportPoint, cropVp.Right, cropVp.Bottom, halfCorner)) 
            return GestureKind.ResizeBottomRight;

        // Edge strips (excluding the corner squares).
        bool withinTopBand = Math.Abs(viewportPoint.Y - cropVp.Top) <= halfEdge;
        bool withinBottomBand = Math.Abs(viewportPoint.Y - cropVp.Bottom) <= halfEdge;
        bool withinLeftBand = Math.Abs(viewportPoint.X - cropVp.Left) <= halfEdge;
        bool withinRightBand = Math.Abs(viewportPoint.X - cropVp.Right) <= halfEdge;
        bool withinHorizontalRange = viewportPoint.X >= cropVp.Left - halfEdge && viewportPoint.X <= cropVp.Right + halfEdge;
        bool withinVerticalRange = viewportPoint.Y >= cropVp.Top - halfEdge && viewportPoint.Y <= cropVp.Bottom + halfEdge;

        if (withinTopBand && withinHorizontalRange) 
            return GestureKind.ResizeTop;

        if (withinBottomBand && withinHorizontalRange) 
            return GestureKind.ResizeBottom;

        if (withinLeftBand && withinVerticalRange) 
            return GestureKind.ResizeLeft;

        if (withinRightBand && withinVerticalRange) 
            return GestureKind.ResizeRight;

        // Inside the crop.
        if (cropVp.Contains(viewportPoint))
        {
            return GestureKind.MoveRect;
        }

        return GestureKind.DrawNew;
    }

    private static bool InBox(Point p, double cx, double cy, double half)
    {
        return Math.Abs(p.X - cx) <= half && Math.Abs(p.Y - cy) <= half;
    }

    private static Point ComputeAnchorImage(GestureKind gesture, Rect crop) => gesture switch
    {
        GestureKind.ResizeTopLeft => new Point(crop.Right, crop.Bottom),
        GestureKind.ResizeTopRight => new Point(crop.Left, crop.Bottom),
        GestureKind.ResizeBottomLeft => new Point(crop.Right, crop.Top),
        GestureKind.ResizeBottomRight => new Point(crop.Left, crop.Top),
        GestureKind.ResizeTop => new Point(crop.Left, crop.Bottom),
        GestureKind.ResizeBottom => new Point(crop.Left, crop.Top),
        GestureKind.ResizeLeft => new Point(crop.Right, crop.Top),
        GestureKind.ResizeRight => new Point(crop.Left, crop.Top),
        _ => new Point(crop.X, crop.Y),
    };

    private static Rect ResizeCrop(GestureKind gesture, Rect startCrop, Point anchor, Point cursor)
    {
        double x1 = anchor.X, y1 = anchor.Y, x2 = cursor.X, y2 = cursor.Y;

        switch (gesture)
        {
            case GestureKind.ResizeTopLeft:
            case GestureKind.ResizeTopRight:
            case GestureKind.ResizeBottomLeft:
            case GestureKind.ResizeBottomRight:
                // Both axes free, anchored at opposite corner.
                break;

            case GestureKind.ResizeTop:
                x1 = startCrop.Left;
                x2 = startCrop.Right;
                y1 = startCrop.Bottom;
                break;
            case GestureKind.ResizeBottom:
                x1 = startCrop.Left;
                x2 = startCrop.Right;
                y1 = startCrop.Top;
                break;
            case GestureKind.ResizeLeft:
                y1 = startCrop.Top;
                y2 = startCrop.Bottom;
                x1 = startCrop.Right;
                break;
            case GestureKind.ResizeRight:
                y1 = startCrop.Top;
                y2 = startCrop.Bottom;
                x1 = startCrop.Left;
                break;
        }

        return RectFromPoints(new Point(x1, y1), new Point(x2, y2));
    }

    private static Rect MoveCrop(Rect startCrop, Vector delta, CropOverlayViewModel vm)
    {
        double newX = startCrop.X + delta.X;
        double newY = startCrop.Y + delta.Y;
        newX = Math.Max(0, Math.Min(newX, vm.ImageWidth - startCrop.Width));
        newY = Math.Max(0, Math.Min(newY, vm.ImageHeight - startCrop.Height));
        return new Rect(newX, newY, startCrop.Width, startCrop.Height);
    }

    private static Rect RectFromPoints(Point a, Point b)
    {
        double left = Math.Min(a.X, b.X);
        double top = Math.Min(a.Y, b.Y);
        double width = Math.Abs(a.X - b.X);
        double height = Math.Abs(a.Y - b.Y);
        return new Rect(left, top, width, height);
    }

    private Point ToImagePoint(Point viewportPoint)
    {
        if (ContentToViewport is { } t && t.Inverse is Transform inverse)
        {
            return inverse.Transform(viewportPoint);
        }
        return viewportPoint;
    }

    private static Point ClampToImage(Point p, CropOverlayViewModel vm)
    {
        return new Point(
            Math.Max(0, Math.Min(p.X, vm.ImageWidth)),
            Math.Max(0, Math.Min(p.Y, vm.ImageHeight)));
    }

    private static bool ContainsInclusive(Rect r, Point p)
    {
        return p.X >= r.X && p.X <= r.Right && p.Y >= r.Y && p.Y <= r.Bottom;
    }

    private static Rect TransformRect(Transform? transform, Rect rect)
    {
        if (transform is null)
        {
            return rect;
        }
        return transform.TransformBounds(rect);
    }

    private static void SetCanvasRect(FrameworkElement element, double x, double y, double width, double height)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private static void SetBracketPoints(Polyline polyline, Point a, Point b, Point c)
    {
        PointCollection points = polyline.Points;
        if (points.Count == 3)
        {
            points[0] = a;
            points[1] = b;
            points[2] = c;
        }
        else
        {
            points.Clear();
            points.Add(a);
            points.Add(b);
            points.Add(c);
        }
    }
}
