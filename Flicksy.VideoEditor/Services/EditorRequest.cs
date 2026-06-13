namespace Flicksy.VideoEditor.Services;

/// <summary>
/// Which editor to open. Carries the runtime choice of project (empty / from a source file /
/// later a saved project) through <see cref="IEditorFactory"/> into a DI-built window, so the
/// old DI-bypassing <c>new VideoEditorViewModel(...)</c> startup path is gone.
/// </summary>
public abstract record EditorRequest
{
    public sealed record Empty : EditorRequest;

    public sealed record FromSourceFile(string Path) : EditorRequest;

    // Future: FromSavedProject(string Path) : EditorRequest — same factory, new arm.
}
