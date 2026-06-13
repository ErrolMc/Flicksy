using Flicksy.VideoEditor.Windows;

namespace Flicksy.VideoEditor.Services;

/// <summary>
/// Builds a fully-wired <see cref="VideoEditorWindow"/> around a runtime-selected project.
/// The single seam where a runtime value (which project to open) meets the DI graph, so
/// startup and the Welcome screen never new up the editor by hand or reach for a service locator.
/// </summary>
public interface IEditorFactory
{
    VideoEditorWindow Create(EditorRequest request);
}
