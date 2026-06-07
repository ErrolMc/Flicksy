using System.Collections.Generic;

namespace Flicksy.Drawing.Undo.Commands;

/// <summary>
/// Bundles several commands into one undo step. Used for batched gestures like drag-erase
/// where many <see cref="RemoveItemCommand"/>s accumulate during a single mouse drag, and
/// (in the video editor) multi-clip delete / move.
///
/// <para>
/// Selection is preserved across the bundle via an optional <see cref="ICompositeSelectionScope"/>:
/// the command captures selection before invoking children and restores it afterward, so inner
/// commands don't leave the surface with a per-step selection that wasn't part of the user's
/// gesture. Pass <c>null</c> when no selection preservation is wanted. The command has no
/// dependency on any particular surface or view-model — both the snip editor and the video
/// editor reuse it by supplying their own scope.
/// </para>
/// </summary>
public sealed class CompositeCommand : IUndoableCommand
{
    private readonly IReadOnlyList<IUndoableCommand> _children;
    private readonly ICompositeSelectionScope? _selectionScope;

    public CompositeCommand(IReadOnlyList<IUndoableCommand> children, ICompositeSelectionScope? selectionScope = null)
    {
        _children = children;
        _selectionScope = selectionScope;
    }

    public int Count => _children.Count;

    public void Redo()
    {
        object? token = _selectionScope?.Capture();
        foreach (var child in _children)
        {
            child.Redo();
        }
        _selectionScope?.Restore(token);
    }

    public void Undo()
    {
        object? token = _selectionScope?.Capture();
        for (var i = _children.Count - 1; i >= 0; i--)
        {
            _children[i].Undo();
        }
        _selectionScope?.Restore(token);
    }
}
