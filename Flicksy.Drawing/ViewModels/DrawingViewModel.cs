using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.Undo;
using Flicksy.Drawing.Undo.Commands;

namespace Flicksy.Drawing.ViewModels;

public partial class DrawingViewModel : ObservableObject
{
    private PenStrokeItem? _currentPen;
    private int _currentPenIndex = -1;
    private ShapeItem? _currentShape;
    private int _currentShapeIndex = -1;

    // Per-text-edit-session state. Captured in BeginEditText; consumed by EndEditText.
    private TextItem? _editOriginalItem;
    private string _editOriginalText = string.Empty;
    private bool _editIsNewItem;
    private int _editOriginalIndex = -1;

    // BeginText sets this so the next BeginEditText knows the edit is for a brand-new item.
    private TextItem? _pendingNewTextItem;

    // Style-edit session state (popup-batched). Captured in BeginTextStyleEdit.
    private TextItem? _styleEditItem;
    private TextStyleSnapshot _styleEditBefore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    [NotifyPropertyChangedFor(nameof(CanMoveSelectedItemUp))]
    [NotifyPropertyChangedFor(nameof(CanMoveSelectedItemDown))]
    [NotifyCanExecuteChangedFor(nameof(MoveSelectedItemUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveSelectedItemDownCommand))]
    private DrawingItem? selectedItem;

    [ObservableProperty]
    private TextItem? editingTextItem;

    public DrawingViewModel()
    {
        Items.CollectionChanged += OnItemsChanged;
        History = new UndoManager();
    }

    public ObservableCollection<DrawingItem> Items { get; } = new();

    public UndoManager History { get; }

    public bool HasItems => Items.Count > 0;

    public bool HasSelectedItem => SelectedItem is not null;

    public bool CanMoveSelectedItemUp => SelectedItem is { } item && Items.IndexOf(item) < Items.Count - 1;

    public bool CanMoveSelectedItemDown => SelectedItem is { } item && Items.IndexOf(item) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveSelectedItemUp))]
    private void MoveSelectedItemUp()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        int index = Items.IndexOf(item);
        if (index < 0 || index >= Items.Count - 1)
        {
            return;
        }

        Items.Move(index, index + 1);
        History.Push(new MoveLayerCommand(this, item, index, index + 1));
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedItemDown))]
    private void MoveSelectedItemDown()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        int index = Items.IndexOf(item);
        if (index <= 0)
        {
            return;
        }

        Items.Move(index, index - 1);
        History.Push(new MoveLayerCommand(this, item, index, index - 1));
    }

    public void BeginPenStroke(Point point, Brush brush, double thickness)
    {
        _currentPen = new PenStrokeItem(brush, thickness);
        _currentPen.AddPoint(point);
        _currentPenIndex = Items.Count;
        Items.Add(_currentPen);
    }

    public void AppendPenPoint(Point point)
    {
        _currentPen?.AddPoint(point);
    }

    public void EndPenStroke()
    {
        if (_currentPen is { } pen && _currentPenIndex >= 0)
        {
            // The stroke is already in Items; the command snapshots the final-state reference
            // so undo removes it and redo re-inserts at the same z-order.
            History.Push(new AddItemCommand(this, pen, _currentPenIndex));
        }

        _currentPen = null;
        _currentPenIndex = -1;
    }

    public void BeginShape(Point point, ShapeKind kind, Brush? fill, Brush? outline, double outlineThickness)
    {
        // Skip if nothing will be visible.
        if (outline is null && (kind is ShapeKind.Line or ShapeKind.Arrow || fill is null))
        {
            _currentShape = null;
            _currentShapeIndex = -1;
            return;
        }

        _currentShape = new ShapeItem(kind, point, fill, outline, outlineThickness);
        _currentShapeIndex = Items.Count;
        Items.Add(_currentShape);
    }

    public void UpdateShapeEndPoint(Point point)
    {
        _currentShape?.UpdateEndPoint(point);
    }

    public void EndShape()
    {
        if (_currentShape is { } shape)
        {
            if (shape.IsDegenerate)
            {
                Items.Remove(shape);
            }
            else if (_currentShapeIndex >= 0)
            {
                History.Push(new AddItemCommand(this, shape, _currentShapeIndex));
            }
        }

        _currentShape = null;
        _currentShapeIndex = -1;
    }

    public TextItem BeginText(Point origin, string fontFamily, double fontSize, Brush? fill, Brush? outline, double outlineThickness)
    {
        var item = new TextItem(origin, fontFamily, fontSize, fill, outline, outlineThickness);
        Items.Add(item);
        SelectedItem = item;
        _pendingNewTextItem = item;
        return item;
    }

    public void BeginEditText(TextItem item)
    {
        if (EditingTextItem is { } current && !ReferenceEquals(current, item))
        {
            EndEditText(commit: true);
        }

        _editOriginalItem = item;
        _editOriginalText = item.Text;
        _editIsNewItem = ReferenceEquals(_pendingNewTextItem, item);
        _editOriginalIndex = _editIsNewItem ? Items.IndexOf(item) : -1;
        _pendingNewTextItem = null;

        SelectedItem = item;
        item.IsEditing = true;
        EditingTextItem = item;
    }

    public void EndEditText(bool commit)
    {
        if (EditingTextItem is not { } item)
        {
            return;
        }

        item.IsEditing = false;
        EditingTextItem = null;

        bool wasNewItem = _editIsNewItem && ReferenceEquals(_editOriginalItem, item);
        string originalText = ReferenceEquals(_editOriginalItem, item) ? _editOriginalText : item.Text;
        int originalIndex = _editOriginalIndex;

        // Clear the captured session state before pushing the command so a re-entrant
        // BeginEditText (e.g. from inside the redo path) doesn't see stale data.
        _editOriginalItem = null;
        _editOriginalText = string.Empty;
        _editIsNewItem = false;
        _editOriginalIndex = -1;

        if (commit && item.IsEmpty)
        {
            Items.Remove(item);
            return;
        }

        if (!commit)
        {
            return;
        }

        if (wasNewItem && originalIndex >= 0)
        {
            History.Push(new AddItemCommand(this, item, originalIndex));
        }
        else if (!wasNewItem && !string.Equals(originalText, item.Text, System.StringComparison.Ordinal))
        {
            History.Push(new TextEditCommand(this, item, originalText, item.Text));
        }
    }

    public void BeginTextStyleEdit(TextItem item)
    {
        _styleEditItem = item;
        _styleEditBefore = TextStyleSnapshot.Capture(item);
    }

    public void EndTextStyleEdit()
    {
        if (_styleEditItem is not { } item)
        {
            return;
        }

        TextStyleSnapshot before = _styleEditBefore;
        _styleEditItem = null;
        _styleEditBefore = default;

        var after = TextStyleSnapshot.Capture(item);
        if (!before.Equals(after) && Items.Contains(item))
        {
            History.Push(new TextStyleCommand(this, item, before, after));
        }
    }

    public void Clear()
    {
        _currentPen = null;
        _currentPenIndex = -1;
        _currentShape = null;
        _currentShapeIndex = -1;
        _editOriginalItem = null;
        _editOriginalText = string.Empty;
        _editIsNewItem = false;
        _editOriginalIndex = -1;
        _pendingNewTextItem = null;
        _styleEditItem = null;
        _styleEditBefore = default;

        if (EditingTextItem is { } editing)
        {
            editing.IsEditing = false;
        }
        EditingTextItem = null;
        SelectedItem = null;
        Items.Clear();
        History.Reset();
    }

    public bool DeleteSelectedItem()
    {
        if (SelectedItem is not { } item)
        {
            return false;
        }

        int index = Items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        // OnItemsChanged will null out SelectedItem when the collection no longer contains it.
        bool removed = Items.Remove(item);
        if (removed)
        {
            History.Push(new RemoveItemCommand(this, item, index));
        }
        return removed;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasItems));

        if (SelectedItem is null)
        {
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Remove ||
            e.Action == NotifyCollectionChangedAction.Replace ||
            e.Action == NotifyCollectionChangedAction.Reset)
        {
            if (!Items.Contains(SelectedItem))
            {
                SelectedItem = null;
                return;
            }
        }

        // The selected item's index may have shifted (move/add/remove), so the layer
        // commands' executability needs to be re-checked.
        OnPropertyChanged(nameof(CanMoveSelectedItemUp));
        OnPropertyChanged(nameof(CanMoveSelectedItemDown));
        MoveSelectedItemUpCommand.NotifyCanExecuteChanged();
        MoveSelectedItemDownCommand.NotifyCanExecuteChanged();
    }
}
