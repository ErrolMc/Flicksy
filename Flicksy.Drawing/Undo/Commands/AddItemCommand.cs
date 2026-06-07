using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Undo.Commands;

public sealed class AddItemCommand : IUndoableCommand
{
    private readonly DrawingViewModel _viewModel;
    private readonly DrawingItem _item;
    private readonly int _index;

    public AddItemCommand(DrawingViewModel viewModel, DrawingItem item, int index)
    {
        _viewModel = viewModel;
        _item = item;
        _index = index;
    }

    public void Redo()
    {
        int insertIndex = _index < 0 || _index > _viewModel.Items.Count
            ? _viewModel.Items.Count
            : _index;
        _viewModel.Items.Insert(insertIndex, _item);
        _viewModel.SelectedItem = _item;
    }

    public void Undo()
    {
        _viewModel.Items.Remove(_item);
        // OnItemsChanged clears SelectedItem automatically if it was the removed one.
    }
}
