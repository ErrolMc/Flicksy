using CommunityToolkit.Mvvm.ComponentModel;

namespace Flicksy.VideoEditor.Services;

/// <summary>
/// App-wide, user-editable editor preferences, persisted to
/// <c>%LOCALAPPDATA%\Flicksy\video-editor.json</c> by <see cref="ISettingsService"/> — the
/// writable, process-wide parallel to the per-document <see cref="Project.ProjectSettings"/>.
/// Observable so the Settings overlay two-way binds straight to it and the service auto-saves
/// on each change.
/// </summary>
public partial class VideoEditorSettings : ObservableObject
{
    /// <summary>
    /// Use GPU (hardware) video decode when available; unchecked forces CPU decode. Read at
    /// startup into <see cref="Flicksy.Drawing.Media.HardwareMediaDecoder.Disabled"/>, so a
    /// change takes effect on the next launch (the ADR 0010 kill switch is set-once).
    /// </summary>
    [ObservableProperty]
    private bool useHardwareDecode = true;

    /// <summary>
    /// Show the performance HUD. Persisted now; the HUD that reads it lands later.
    /// </summary>
    [ObservableProperty]
    private bool showPerformanceStats = false;
}
