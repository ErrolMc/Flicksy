using System;
using System.Windows;
using System.Windows.Input;
using Flicksy.Drawing.Interaction.Config;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Interaction.Tools;

/// <summary>
/// Drag-to-draw shape gesture (square / circle / line / arrow). Down captures the start
/// point and shape kind, move updates the end point — with Shift constraining to perfect
/// squares/circles or 45° angle snaps — and up commits or discards a degenerate shape.
/// Depends only on <see cref="IDrawingSurface"/> + <see cref="DrawingViewModel"/> +
/// <see cref="IShapeConfig"/>.
/// </summary>
public sealed class ShapeTool : IDrawingTool
{
    private readonly IDrawingSurface _surface;
    private readonly DrawingViewModel _viewModel;
    private readonly IShapeConfig _config;

    private bool _drawing;
    private Point _startPoint;
    private ShapeKind _kindInProgress;

    public ShapeTool(IDrawingSurface surface, DrawingViewModel viewModel, IShapeConfig config)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public bool IsActive => _drawing;

    public bool OnPointerDown(Point point, MouseButtonEventArgs e)
    {
        _kindInProgress = _config.ActiveShape;
        _startPoint = point;
        _drawing = true;
        _viewModel.BeginShape(point, _kindInProgress, _config.FillBrush, _config.OutlineBrush, _config.OutlineThickness);
        _surface.CapturePointer();
        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        // Move uses the raw (unclamped) point so the cursor stays responsive when the user
        // drags past the canvas edge — matches the original DrawingView behavior.
        _viewModel.UpdateShapeEndPoint(ResolveEndPoint(point));
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        _viewModel.UpdateShapeEndPoint(ResolveEndPoint(point));
        _viewModel.EndShape();
        _drawing = false;
        _surface.ReleasePointer();
        e.Handled = true;
    }

    public void OnPointerHover(Point point, MouseEventArgs e)
    {
        // Shape tool has no hover affordance.
    }

    private Point ResolveEndPoint(Point rawCurrent)
        => (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
            ? ApplyShiftConstraint(_kindInProgress, _startPoint, rawCurrent)
            : rawCurrent;

    private static Point ApplyShiftConstraint(ShapeKind kind, Point start, Point end)
    {
        switch (kind)
        {
            case ShapeKind.Square:
            case ShapeKind.Circle:
            {
                // Constrain to equal side lengths (perfect square / circle), preserving each axis sign.
                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double size = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int signX = dx < 0 ? -1 : 1;
                int signY = dy < 0 ? -1 : 1;
                return new Point(start.X + signX * size, start.Y + signY * size);
            }
            case ShapeKind.Line:
            case ShapeKind.Arrow:
            {
                // Snap angle to the nearest 45°, preserve length.
                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= double.Epsilon)
                {
                    return end;
                }

                double step = Math.PI / 4.0;
                double angle = Math.Atan2(dy, dx);
                double snapped = Math.Round(angle / step) * step;
                return new Point(start.X + Math.Cos(snapped) * length, start.Y + Math.Sin(snapped) * length);
            }
            default:
                return end;
        }
    }
}
