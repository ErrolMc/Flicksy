namespace Flicksy.VideoEditor.Services;

/// <summary>
/// DI-injectable handle to the active document's project settings. There is one source of truth
/// — the <see cref="Project.Project"/>'s own Settings instance — this service just makes it
/// reachable without threading a Project reference around. Seeded when an editor opens (by
/// <see cref="IEditorFactory"/>) and read by the title bar's Project Settings overlay. Singleton
/// today (one document per process); becomes Scoped (one per document) when tabs/MDI land.
/// </summary>
public interface IProjectSettingsService
{
    Project.ProjectSettings Current { get; }
}
