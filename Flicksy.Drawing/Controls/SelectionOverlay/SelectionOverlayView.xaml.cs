using System;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Controls;

public partial class SelectionOverlayView : UserControl
{
    private const double HandleRadius = 4.0;
    private const double RotateHandleSize = 26.0;
    private const double RotateHandleGap = 18.0;

    private static readonly ImageSource RotateIconSource = LoadRotateIcon();

    private DrawingItem? _rotatingItem;
    private Matrix _rotationBaseMatrix;
    private Point _rotationCenterWorld;
    private Point _rotationCenterViewport;
    private double _rotationInitialAngle;

    public static readonly DependencyProperty ContentToViewportProperty =
        DependencyProperty.Register(
            nameof(ContentToViewport),
            typeof(Transform),
            typeof(SelectionOverlayView),
            new PropertyMetadata(null, OnContentToViewportChanged));

    public SelectionOverlayView()
    {
        InitializeComponent();
        RotateHandleIcon.Source = RotateIconSource;
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => UpdateLayoutFromState();
    }

    public Transform? ContentToViewport
    {
        get => (Transform?)GetValue(ContentToViewportProperty);
        set => SetValue(ContentToViewportProperty, value);
    }

    private SelectionOverlayViewModel? ViewModel => DataContext as SelectionOverlayViewModel;

    private static void OnContentToViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (SelectionOverlayView)d;
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

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SelectionOverlayViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            oldVm.TransformChanged -= OnVmTransformChanged;
        }

        if (e.NewValue is SelectionOverlayViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            newVm.TransformChanged += OnVmTransformChanged;
        }

        UpdateLayoutFromState();
    }

    private void OnVmTransformChanged(object? sender, EventArgs e)
    {
        UpdateLayoutFromState();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SelectionOverlayViewModel.CanonicalBounds)
            or nameof(SelectionOverlayViewModel.IsVisible)
            or nameof(SelectionOverlayViewModel.SelectedItem)
            or nameof(SelectionOverlayViewModel.IsActive)
            or nameof(SelectionOverlayViewModel.ShowHandles))
        {
            UpdateLayoutFromState();
        }
    }

    private void UpdateLayoutFromState()
    {
        SelectionOverlayViewModel? vm = ViewModel;
        if (vm is null || !vm.IsVisible || vm.SelectedItem is not { } item)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;

        Rect canonical = vm.CanonicalBounds;
        Matrix itemMatrix = item.Transform.Matrix;
        Transform? viewportTransform = ContentToViewport;

        Point tlVp = ProjectCorner(canonical.Left, canonical.Top, itemMatrix, viewportTransform);
        Point trVp = ProjectCorner(canonical.Right, canonical.Top, itemMatrix, viewportTransform);
        Point brVp = ProjectCorner(canonical.Right, canonical.Bottom, itemMatrix, viewportTransform);
        Point blVp = ProjectCorner(canonical.Left, canonical.Bottom, itemMatrix, viewportTransform);

        SelectionBox.Points = new PointCollection { tlVp, trVp, brVp, blVp };

        var handleVisibility = vm.ShowHandles ? Visibility.Visible : Visibility.Collapsed;
        HandleTopLeft.Visibility = handleVisibility;
        HandleTopRight.Visibility = handleVisibility;
        HandleBottomLeft.Visibility = handleVisibility;
        HandleBottomRight.Visibility = handleVisibility;
        RotateHandle.Visibility = handleVisibility;

        PositionHandle(HandleTopLeft, tlVp);
        PositionHandle(HandleTopRight, trVp);
        PositionHandle(HandleBottomLeft, blVp);
        PositionHandle(HandleBottomRight, brVp);

        // Rotate puck: midpoint of top edge in viewport space, offset perpendicular to that edge.
        var topMid = new Point((tlVp.X + trVp.X) / 2.0, (tlVp.Y + trVp.Y) / 2.0);
        var topEdge = new Vector(trVp.X - tlVp.X, trVp.Y - tlVp.Y);
        double len = topEdge.Length;
        Vector outward = len > double.Epsilon
            ? new Vector(topEdge.Y / len, -topEdge.X / len)   // 90° CW of top-edge direction = outward "up"
            : new Vector(0, -1);

        double puckOffset = RotateHandleGap + RotateHandleSize / 2.0;
        Point puckCenter = topMid + outward * puckOffset;
        Canvas.SetLeft(RotateHandle, puckCenter.X - RotateHandleSize / 2.0);
        Canvas.SetTop(RotateHandle, puckCenter.Y - RotateHandleSize / 2.0);
    }

    private static Point ProjectCorner(double x, double y, Matrix itemMatrix, Transform? viewportTransform)
    {
        Point content = itemMatrix.Transform(new Point(x, y));
        return viewportTransform is not null
            ? viewportTransform.Transform(content)
            : content;
    }

    private static void PositionHandle(Ellipse handle, Point point)
    {
        Canvas.SetLeft(handle, point.X - HandleRadius);
        Canvas.SetTop(handle, point.Y - HandleRadius);
    }

    private void OnRotateHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } vm || vm.SelectedItem is not { } item || vm.CanonicalBounds.IsEmpty)
        {
            return;
        }

        Rect canonical = vm.CanonicalBounds;
        var canonicalCenter = new Point(
            canonical.X + canonical.Width / 2.0,
            canonical.Y + canonical.Height / 2.0);

        Matrix itemMatrix = item.Transform.Matrix;
        Point centerWorld = itemMatrix.Transform(canonicalCenter);

        Transform? viewportTransform = ContentToViewport;
        Point centerViewport = viewportTransform is not null
            ? viewportTransform.Transform(centerWorld)
            : centerWorld;

        Point cursor = e.GetPosition(this);

        _rotatingItem = item;
        _rotationBaseMatrix = itemMatrix;
        _rotationCenterWorld = centerWorld;
        _rotationCenterViewport = centerViewport;
        _rotationInitialAngle = Math.Atan2(cursor.Y - centerViewport.Y, cursor.X - centerViewport.X);

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_rotatingItem is null)
        {
            return;
        }

        Point cursor = e.GetPosition(this);
        double currentAngle = Math.Atan2(cursor.Y - _rotationCenterViewport.Y, cursor.X - _rotationCenterViewport.X);
        double deltaDegrees = (currentAngle - _rotationInitialAngle) * 180.0 / Math.PI;
        _rotatingItem.RotateFrom(_rotationBaseMatrix, deltaDegrees, _rotationCenterWorld);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_rotatingItem is null)
        {
            return;
        }

        _rotatingItem = null;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private static ImageSource LoadRotateIcon()
    {
        using var stream = new MemoryStream();
        Images.rotate.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
