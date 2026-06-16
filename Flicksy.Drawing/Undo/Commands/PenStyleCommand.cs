using System.Windows.Media;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Undo.Commands;

public readonly record struct PenStyleSnapshot(
    Brush Brush,
    double Thickness)
{
    public static PenStyleSnapshot Capture(PenStrokeItem item) => new(
        item.Brush,
        item.Thickness);

    public void ApplyTo(PenStrokeItem item)
    {
        item.SetStyle(Brush, Thickness);
    }
}

public sealed class PenStyleCommand : IUndoableCommand
{
    private readonly DrawingViewModel _viewModel;
    private readonly PenStrokeItem _item;
    private readonly PenStyleSnapshot _before;
    private readonly PenStyleSnapshot _after;

    public PenStyleCommand(DrawingViewModel viewModel, PenStrokeItem item, PenStyleSnapshot before, PenStyleSnapshot after)
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
