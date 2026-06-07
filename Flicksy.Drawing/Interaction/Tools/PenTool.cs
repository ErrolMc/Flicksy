using System;
using System.Windows;
using System.Windows.Input;
using Flicksy.Drawing.Interaction.Config;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Interaction.Tools;

/// <summary>
/// Free-hand pen stroke gesture: down begins a stroke, move appends smoothed points (with a
/// minimum-distance gate so we don't spam the geometry with sub-pixel samples), up ends the
/// stroke. Depends only on <see cref="IDrawingSurface"/> + <see cref="DrawingViewModel"/> +
/// <see cref="IPenConfig"/> so it can be reused by any host that supplies a colour /
/// thickness pair.
/// </summary>
public sealed class PenTool : IDrawingTool
{
    private readonly IDrawingSurface _surface;
    private readonly DrawingViewModel _viewModel;
    private readonly IPenConfig _config;
    private readonly InputSmoothing _smoothing = new();

    private Point? _lastAppendedPoint;
    private bool _stroking;

    public PenTool(IDrawingSurface surface, DrawingViewModel viewModel, IPenConfig config)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public bool IsActive => _stroking;

    public bool OnPointerDown(Point point, MouseButtonEventArgs e)
    {
        _viewModel.BeginPenStroke(point, _config.StrokeBrush, _config.StrokeThickness);
        _lastAppendedPoint = point;
        _smoothing.Seed(point);
        _stroking = true;
        _surface.CapturePointer();
        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        // Move uses a clamped-to-bounds point so strokes can't run off the canvas edge. If
        // the surface reports no valid point (canvas not yet sized), skip this sample.
        if (!_surface.TryGetCanvasPoint(e, clampToBounds: true, out var clamped))
        {
            return;
        }

        Point smoothed = _smoothing.Smooth(clamped);

        if (_lastAppendedPoint is Point lastPoint)
        {
            // Minimum-distance gate scales with stroke thickness so thick brushes don't
            // accumulate redundant interior points.
            double minDistance = Math.Max(1.5d, _config.StrokeThickness * 0.5d);
            double dx = smoothed.X - lastPoint.X;
            double dy = smoothed.Y - lastPoint.Y;
            if ((dx * dx) + (dy * dy) < (minDistance * minDistance))
            {
                return;
            }
        }

        _viewModel.AppendPenPoint(smoothed);
        _lastAppendedPoint = smoothed;
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        if (_surface.TryGetCanvasPoint(e, clampToBounds: true, out var clamped))
        {
            _viewModel.AppendPenPoint(_smoothing.Smooth(clamped));
        }

        _viewModel.EndPenStroke();
        _surface.ReleasePointer();
        _lastAppendedPoint = null;
        _smoothing.Reset();
        _stroking = false;
    }

    public void OnPointerHover(Point point, MouseEventArgs e)
    {
        // Pen tool has no hover affordance.
    }
}
