using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flicksy.Drawing.Controllers;
using Flicksy.Drawing.Interaction;
using Flicksy.Drawing.Interaction.Config;
using Flicksy.Drawing.Interaction.Tools;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Controls;

public partial class DrawingView : UserControl, IDrawingSurface, IPenConfig, IShapeConfig, ITextConfig
{
    private readonly TextEditingController _textEditing;
    private readonly ToolRouter _router;
    private SelectTool? _selectTool;
    private PenTool? _penTool;
    private EraseTool? _eraseTool;
    private ShapeTool? _shapeTool;
    private TextTool? _textTool;

    public DrawingView()
    {
        InitializeComponent();
        _textEditing = new TextEditingController(EditTextBox, () => ViewModel);
        // The selector picks the tool that matches the user's current mode. Pen is the
        // default fall-through when no other mode is active.
        _router = new ToolRouter(() =>
            IsSelectActive ? _selectTool :
            IsErasing ? _eraseTool :
            IsShapeActive ? _shapeTool :
            IsTextActive ? _textTool :
            _penTool);
        DataContextChanged += OnDataContextChanged;
    }

    public DrawingViewModel? ViewModel => DataContext as DrawingViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _textEditing.OnHostDataContextChanged(e.OldValue as DrawingViewModel, e.NewValue as DrawingViewModel);

        // Rebuild the tools whenever the bound view-model changes. Tools live as long as the
        // document they operate on, so a doc swap means a fresh set of tools — and we wipe
        // the router so the registration list doesn't accumulate stale entries.
        _router.ClearRegistrations();
        if (e.NewValue is DrawingViewModel newVm)
        {
            _selectTool = new SelectTool(this, newVm);
            _penTool = new PenTool(this, newVm, this);
            _eraseTool = new EraseTool(this, newVm);
            _shapeTool = new ShapeTool(this, newVm, this);
            _textTool = new TextTool(newVm, this);
            _router.Register(_selectTool);
            _router.Register(_penTool);
            _router.Register(_eraseTool);
            _router.Register(_shapeTool);
            _router.Register(_textTool);
        }
        else
        {
            _selectTool = null;
            _penTool = null;
            _eraseTool = null;
            _shapeTool = null;
            _textTool = null;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || !TryGetPoint(e, clampToBounds: false, out var point))
        {
            return;
        }

        // All modes now flow through the router; the selector picks the right tool based on
        // the current mode.
        _router.OnPointerDown(point, e);
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        // Active gesture on a controller-pattern tool wins regardless of which mode the user
        // is in, so move events always reach the tool that started the gesture.
        if (_router.HasActiveGesture)
        {
            _router.OnPointerMove(e.GetPosition(this), e);
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (IsSelectActive)
            {
                _router.OnPointerHover(e.GetPosition(this), e);
            }
            else
            {
                Cursor = null;
            }
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_router.HasActiveGesture)
        {
            _router.OnPointerUp(e.GetPosition(this), e);
        }
    }

    private bool TryGetPoint(MouseEventArgs e, bool clampToBounds, out Point point)
    {
        double x = ActualWidth;
        double y = ActualHeight;
        if (x <= 0 || y <= 0)
        {
            point = default;
            return false;
        }

        Point position = e.GetPosition(this);
        if (!clampToBounds && (position.X < 0 || position.Y < 0 || position.X > x || position.Y > y))
        {
            point = default;
            return false;
        }

        point = new Point(
            Math.Clamp(position.X, 0, x),
            Math.Clamp(position.Y, 0, y));

        return true;
    }

    // ---------- IDrawingSurface / IPenConfig / IShapeConfig / ITextConfig ----------
    // These forward to existing members so tool controllers can depend on the small
    // interface contracts instead of the DrawingView control type.

    void IDrawingSurface.CapturePointer() => CaptureMouse();

    void IDrawingSurface.ReleasePointer() => ReleaseMouseCapture();

    bool IDrawingSurface.TryGetCanvasPoint(MouseEventArgs e, bool clampToBounds, out Point point)
        => TryGetPoint(e, clampToBounds, out point);

    Cursor? IDrawingSurface.Cursor
    {
        get => Cursor;
        set => Cursor = value;
    }
}
