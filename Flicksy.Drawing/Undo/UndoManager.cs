using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Undo.Commands;

namespace Flicksy.Drawing.Undo;

public partial class UndoManager : ObservableObject, IUndoService
{
    private const int MaxEntries = 100;

    private readonly LinkedList<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    // When non-null, Push captures into this list instead of the undo stack; Commit collapses
    // the batch into one undo step. Lets a multi-command gesture (e.g. placing a graphics object
    // = clip-add + item-add) be a single Ctrl+Z.
    private List<IUndoableCommand>? _batch;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool canUndo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    private bool canRedo;

    public void Push(IUndoableCommand command)
    {
        if (_batch is not null)
        {
            _batch.Add(command);
            return;
        }

        _undo.AddLast(command);
        while (_undo.Count > MaxEntries)
        {
            _undo.RemoveFirst();
        }
        _redo.Clear();
        RefreshFlags();
    }

    /// <summary>
    /// Begins an atomic batch: subsequent <see cref="Push"/> calls accumulate until
    /// <see cref="Commit"/> (one undo step) or <see cref="Cancel"/>. Not re-entrant — a Begin
    /// already in progress is replaced.
    /// </summary>
    public void Begin()
    {
        _batch = new List<IUndoableCommand>();
    }

    /// <summary>
    /// Ends the batch and pushes it as one undo step: nothing if empty, the lone command if one,
    /// otherwise a <see cref="CompositeCommand"/> over all of them (selection preserved via the
    /// optional scope), mirroring EraseTool's drag-bundle collapse.
    /// </summary>
    public void Commit(ICompositeSelectionScope? selectionScope = null)
    {
        List<IUndoableCommand>? batch = _batch;
        _batch = null;
        if (batch is null || batch.Count == 0)
        {
            return;
        }

        if (batch.Count == 1)
        {
            Push(batch[0]);
        }
        else
        {
            Push(new CompositeCommand(batch, selectionScope));
        }
    }

    /// <summary>Discards an in-progress batch without pushing anything (e.g. an aborted placement).</summary>
    public void Cancel()
    {
        _batch = null;
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
