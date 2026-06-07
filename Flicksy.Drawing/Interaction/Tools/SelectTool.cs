using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Flicksy.Drawing.Helpers;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.Undo.Commands;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Interaction.Tools;

/// <summary>
/// Click-to-select, drag-to-move, and corner-drag-to-scale gestures for items already in the
/// drawing. Also opens the in-place text editor on double-click of a <see cref="TextItem"/>.
/// Depends only on <see cref="IDrawingSurface"/> + <see cref="DrawingViewModel"/> so it can be
/// reused by any future host (e.g. the video editor).
/// </summary>
public sealed class SelectTool : IDrawingTool
{
    private enum CornerKind { None, TopLeft, TopRight, BottomLeft, BottomRight }

    private readonly IDrawingSurface _surface;
    private readonly DrawingViewModel _viewModel;

    // Drag gesture state.
    private DrawingItem? _draggingItem;
    private Matrix _moveBaseMatrix;
    private Point _moveStartPoint;

    // Scale gesture state.
    private DrawingItem? _scalingItem;
    private Matrix _scaleBaseMatrix;
    private Point _scaleAnchorWorld;
    private Point _scaleOriginalGrabbedWorld;

    public SelectTool(IDrawingSurface surface, DrawingViewModel viewModel)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public bool IsActive => _draggingItem is not null || _scalingItem is not null;

    public bool OnPointerDown(Point point, MouseButtonEventArgs e)
    {
        // Double-click on a TextItem -> open the in-place text editor without starting a drag.
        if (e.ClickCount >= 2 && DrawingMath.HitTestTopmost(_viewModel.Items, point) is TextItem doubleClickedText)
        {
            _viewModel.BeginEditText(doubleClickedText);
            return true;
        }

        // Corner-handle drag of the currently selected item -> begin scaling.
        if (_viewModel.SelectedItem is { } current)
        {
            CornerKind corner = GetCornerHit(current, point);
            if (corner != CornerKind.None)
            {
                Rect canonical = current.CanonicalBounds;
                (Point anchorCanonical, Point grabbedCanonical) = GetAnchorAndGrabbed(canonical, corner);
                Matrix m = current.Transform.Matrix;
                _scalingItem = current;
                _scaleBaseMatrix = m;
                _scaleAnchorWorld = m.Transform(anchorCanonical);
                _scaleOriginalGrabbedWorld = m.Transform(grabbedCanonical);
                _surface.CapturePointer();
                return true;
            }
        }

        DrawingItem? hit = DrawingMath.HitTestTopmost(_viewModel.Items, point);
        if (hit is not null)
        {
            if (hit == _viewModel.SelectedItem)
            {
                _draggingItem = hit;
                _moveBaseMatrix = hit.Transform.Matrix;
                _moveStartPoint = point;
                _surface.CapturePointer();
            }
            else
            {
                _viewModel.SelectedItem = hit;
            }
        }
        else if (_viewModel.SelectedItem is { } selected && IsInsideSelectionBounds(selected, point))
        {
            _draggingItem = selected;
            _moveBaseMatrix = selected.Transform.Matrix;
            _moveStartPoint = point;
            _surface.CapturePointer();
        }
        else
        {
            _viewModel.SelectedItem = null;
        }

        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        if (_scalingItem is not null)
        {
            Vector oldDiag = _scaleOriginalGrabbedWorld - _scaleAnchorWorld;
            double oldLengthSq = oldDiag.X * oldDiag.X + oldDiag.Y * oldDiag.Y;
            if (oldLengthSq > double.Epsilon)
            {
                Vector offset = point - _scaleAnchorWorld;
                double factor = (offset.X * oldDiag.X + offset.Y * oldDiag.Y) / oldLengthSq;
                if (Math.Abs(factor) < 0.01d)
                {
                    factor = factor < 0 ? -0.01d : 0.01d;
                }
                _scalingItem.ScaleFrom(_scaleBaseMatrix, factor, _scaleAnchorWorld);
            }
            return;
        }

        if (_draggingItem is not null)
        {
            Vector totalDelta = point - _moveStartPoint;
            _draggingItem.TranslateFrom(_moveBaseMatrix, totalDelta);
        }
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        if (_scalingItem is { } scaling)
        {
            var finalMatrix = scaling.Transform.Matrix;
            if (!finalMatrix.Equals(_scaleBaseMatrix))
            {
                _viewModel.History.Push(new TransformCommand(_viewModel, scaling, _scaleBaseMatrix, finalMatrix));
            }
            _scalingItem = null;
            _surface.ReleasePointer();
            e.Handled = true;
            return;
        }

        if (_draggingItem is { } dragging)
        {
            var finalMatrix = dragging.Transform.Matrix;
            if (!finalMatrix.Equals(_moveBaseMatrix))
            {
                _viewModel.History.Push(new TransformCommand(_viewModel, dragging, _moveBaseMatrix, finalMatrix));
            }
            _draggingItem = null;
            _surface.ReleasePointer();
            e.Handled = true;
        }
    }

    public void OnPointerHover(Point point, MouseEventArgs e)
    {
        if (_viewModel.SelectedItem is not { } selected)
        {
            _surface.Cursor = null;
            return;
        }

        var corner = GetCornerHit(selected, point);
        if (corner == CornerKind.None)
        {
            _surface.Cursor = null;
            return;
        }

        // Use a unit diagonal based on which corner is grabbed (independent of the item's
        // aspect ratio), then rotate it by the item's rotation so cursors stay diagonal on
        // corners regardless of width/height — only the rotation changes the cursor bucket.
        var unitDiagonal = corner switch
        {
            CornerKind.TopLeft => new Vector(-1, -1),
            CornerKind.TopRight => new Vector(1, -1),
            CornerKind.BottomLeft => new Vector(-1, 1),
            CornerKind.BottomRight => new Vector(1, 1),
            _ => new Vector(1, 1),
        };

        var m = selected.Transform.Matrix;
        var rotation = Math.Atan2(m.M12, m.M11);
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        var rotated = new Vector(
            unitDiagonal.X * cos - unitDiagonal.Y * sin,
            unitDiagonal.X * sin + unitDiagonal.Y * cos);

        _surface.Cursor = DrawingMath.CursorForDiagonal(rotated);
    }

    private CornerKind GetCornerHit(DrawingItem item, Point worldPoint)
    {
        var canonical = item.CanonicalBounds;
        if (canonical.IsEmpty || canonical.Width < 0.5d || canonical.Height < 0.5d)
        {
            return CornerKind.None;
        }

        var m = item.Transform.Matrix;
        var tl = m.Transform(new Point(canonical.Left, canonical.Top));
        var tr = m.Transform(new Point(canonical.Right, canonical.Top));
        var bl = m.Transform(new Point(canonical.Left, canonical.Bottom));
        var br = m.Transform(new Point(canonical.Right, canonical.Bottom));

        var scale = Math.Max(0.0001d, _surface.ContentScale);
        var pickup = 8.0d / scale;
        var pickupSquared = pickup * pickup;

        if (DrawingMath.DistanceSquared(worldPoint, tl) <= pickupSquared) return CornerKind.TopLeft;
        if (DrawingMath.DistanceSquared(worldPoint, tr) <= pickupSquared) return CornerKind.TopRight;
        if (DrawingMath.DistanceSquared(worldPoint, bl) <= pickupSquared) return CornerKind.BottomLeft;
        if (DrawingMath.DistanceSquared(worldPoint, br) <= pickupSquared) return CornerKind.BottomRight;

        return CornerKind.None;
    }

    private static (Point Anchor, Point Grabbed) GetAnchorAndGrabbed(Rect bounds, CornerKind corner) => corner switch
    {
        CornerKind.TopLeft => (new Point(bounds.Right, bounds.Bottom), new Point(bounds.Left, bounds.Top)),
        CornerKind.TopRight => (new Point(bounds.Left, bounds.Bottom), new Point(bounds.Right, bounds.Top)),
        CornerKind.BottomLeft => (new Point(bounds.Right, bounds.Top), new Point(bounds.Left, bounds.Bottom)),
        CornerKind.BottomRight => (new Point(bounds.Left, bounds.Top), new Point(bounds.Right, bounds.Bottom)),
        _ => (default, default),
    };

    private static bool IsInsideSelectionBounds(DrawingItem item, Point worldPoint)
    {
        var canonical = item.CanonicalBounds;
        if (canonical.IsEmpty)
        {
            return false;
        }

        var inverse = item.Transform.Matrix;
        if (!inverse.HasInverse)
        {
            return false;
        }
        inverse.Invert();
        var localPoint = inverse.Transform(worldPoint);
        return canonical.Contains(localPoint);
    }
}
