namespace Flicksy.Drawing.Undo;

/// <summary>
/// Optional selection-preservation hook for <see cref="Commands.CompositeCommand"/>. A bundle
/// captures the current selection before running its children and restores it afterward, so a
/// multi-step undo/redo doesn't leave the surface with a per-child selection that wasn't part
/// of the user's gesture. The token is opaque — the scope owns its own validity check (e.g.
/// "is this item still in the collection?") when restoring.
/// <para>
/// Each surface supplies its own implementation: <see cref="Commands.DrawingSelectionScope"/>
/// for the snip editor (single <c>SelectedItem</c>); the video editor supplies one over its
/// clip selection. Passing <c>null</c> to the command disables selection preservation entirely.
/// </para>
/// </summary>
public interface ICompositeSelectionScope
{
    /// <summary>
    /// Snapshots the current selection as an opaque token to be handed back to
    /// <see cref="Restore"/> once the bundle's children have run.
    /// </summary>
    object? Capture();

    /// <summary>
    /// Restores selection from a token produced by <see cref="Capture"/>. The scope decides
    /// whether the captured selection is still valid (children may have inserted or removed it)
    /// and falls back to clearing selection when it isn't.
    /// </summary>
    void Restore(object? token);
}
