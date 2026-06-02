using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Flicksy.Drawing.Helpers;
using Flicksy.Drawing.Undo;
using Flicksy.Drawing.Undo.Commands;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Interaction.Tools;

/// <summary>
/// Click / drag erase gesture: deletes the topmost item under the pointer on down, then
/// continues to delete items as the pointer drags across them. Depends only on
/// <see cref="IDrawingSurface"/> + <see cref="DrawingViewModel"/>.
/// </summary>
public sealed class EraseTool : IDrawingTool
{
    private readonly IDrawingSurface _surface;
    private readonly DrawingViewModel _viewModel;
    private readonly List<IUndoableCommand> _gestureRemovals = new();

    private bool _erasing;

    public EraseTool(IDrawingSurface surface, DrawingViewModel viewModel)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public bool IsActive => _erasing;

    public bool OnPointerDown(Point point, MouseButtonEventArgs e)
    {
        _gestureRemovals.Clear();
        EraseAt(point);
        _erasing = true;
        _surface.CapturePointer();
        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        if (_surface.TryGetCanvasPoint(e, clampToBounds: true, out var clamped))
        {
            EraseAt(clamped);
        }
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        if (_surface.TryGetCanvasPoint(e, clampToBounds: true, out var clamped))
        {
            EraseAt(clamped);
        }

        if (_gestureRemovals.Count == 1)
        {
            _viewModel.History.Push(_gestureRemovals[0]);
        }
        else if (_gestureRemovals.Count > 1)
        {
            _viewModel.History.Push(new CompositeCommand(
                _gestureRemovals.ToArray(),
                new DrawingSelectionScope(_viewModel)));
        }
        _gestureRemovals.Clear();

        _surface.ReleasePointer();
        _erasing = false;
    }

    public void OnPointerHover(Point point, MouseEventArgs e)
    {
        // Erase tool has no hover affordance.
    }

    private void EraseAt(Point point)
    {
        if (DrawingMath.HitTestTopmost(_viewModel.Items, point) is { } hit)
        {
            var index = _viewModel.Items.IndexOf(hit);
            if (index < 0)
            {
                return;
            }
            _viewModel.Items.Remove(hit);
            _gestureRemovals.Add(new RemoveItemCommand(_viewModel, hit, index));
        }
    }
}
