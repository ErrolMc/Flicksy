using CommunityToolkit.Mvvm.Input;

namespace Flicksy.Drawing.Undo;

/// <summary>
/// A per-document undo/redo stack. Edit commands <see cref="Push"/> onto it after they have
/// already taken effect (gestures mutate live); the shell's Undo/Redo — Edit menu and
/// Ctrl+Z/Ctrl+Y — bind <see cref="UndoCommand"/>/<see cref="RedoCommand"/>. Implemented by
/// <see cref="UndoManager"/>, which stays the concrete bindable type where XAML needs the
/// generated commands; code that only records or queries history depends on this interface.
/// </summary>
public interface IUndoService
{
    /// <summary>Records an already-applied command and clears the redo stack.</summary>
    void Push(IUndoableCommand command);

    /// <summary>Clears both stacks (e.g. when a new document is opened).</summary>
    void Reset();

    bool CanUndo { get; }

    bool CanRedo { get; }

    IRelayCommand UndoCommand { get; }

    IRelayCommand RedoCommand { get; }
}
