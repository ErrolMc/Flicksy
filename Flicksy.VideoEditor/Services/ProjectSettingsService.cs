namespace Flicksy.VideoEditor.Services;

/// <summary>
/// <see cref="IProjectSettingsService"/> backed by the active document's own settings instance —
/// not a copy, so it stays a single source of truth. <see cref="Current"/> is seeded once when
/// the editor is created (the factory sets it from the chosen Project; the design-time VM ctor
/// sets it directly). The setter is concrete-only; consumers read through the interface.
/// </summary>
internal sealed class ProjectSettingsService : IProjectSettingsService
{
    public Project.ProjectSettings Current { get; set; } = new();
}
