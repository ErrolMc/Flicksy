using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flicksy.Drawing.Undo;

public partial class UndoManager : ObservableObject
{
    private const int MaxEntries = 100;

    private readonly LinkedList<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool canUndo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    private bool canRedo;

    public void Push(IUndoableCommand command)
    {
        _undo.AddLast(command);
        while (_undo.Count > MaxEntries)
        {
            _undo.RemoveFirst();
        }
        _redo.Clear();
        RefreshFlags();
    }

    public void Reset()
    {
        _undo.Clear();
        _redo.Clear();
        RefreshFlags();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo.Last is not { } node)
        {
            return;
        }

        IUndoableCommand command = node.Value;
        _undo.RemoveLast();
        command.Undo();
        _redo.Push(command);
        RefreshFlags();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        IUndoableCommand command = _redo.Pop();
        command.Redo();
        _undo.AddLast(command);
        RefreshFlags();
    }

    private void RefreshFlags()
    {
        CanUndo = _undo.Count > 0;
        CanRedo = _redo.Count > 0;
    }
}
