using System.Windows.Media;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Undo.Commands;

public readonly record struct ShapeStyleSnapshot(
    Brush? Fill,
    Brush? Outline,
    double OutlineThickness)
{
    public static ShapeStyleSnapshot Capture(ShapeItem item) => new(
        item.Fill,
        item.Outline,
        item.OutlineThickness);

    public void ApplyTo(ShapeItem item)
    {
        item.SetFill(Fill);
        item.SetOutline(Outline, OutlineThickness);
    }
}

public sealed class ShapeStyleCommand : IUndoableCommand
{
    private readonly DrawingViewModel _viewModel;
    private readonly ShapeItem _item;
    private readonly ShapeStyleSnapshot _before;
    private readonly ShapeStyleSnapshot _after;

    public ShapeStyleCommand(DrawingViewModel viewModel, ShapeItem item, ShapeStyleSnapshot before, ShapeStyleSnapshot after)
    {
        _viewModel = viewModel;
        _item = item;
        _before = before;
        _after = after;
    }

    public void Redo()
    {
        _after.ApplyTo(_item);
        _viewModel.SelectedItem = _item;
    }

    public void Undo()
    {
        _before.ApplyTo(_item);
        _viewModel.SelectedItem = _item;
    }
}
