using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Undo.Commands;

/// <summary>
/// <see cref="ICompositeSelectionScope"/> for the snip editor: preserves the single
/// <see cref="DrawingViewModel.SelectedItem"/> across a <see cref="CompositeCommand"/> bundle,
/// restoring it only if it's still in the collection after the children run (children may have
/// inserted or removed it).
/// </summary>
public sealed class DrawingSelectionScope : ICompositeSelectionScope
{
    private readonly DrawingViewModel _viewModel;

    public DrawingSelectionScope(DrawingViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public object? Capture() => _viewModel.SelectedItem;

    public void Restore(object? token)
    {
        var selectionBefore = token as DrawingItem;
        _viewModel.SelectedItem =
            selectionBefore is not null && _viewModel.Items.Contains(selectionBefore)
                ? selectionBefore
                : null;
    }
}
