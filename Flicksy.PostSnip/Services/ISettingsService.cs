namespace Flicksy.PostSnip.Services;

/// <summary>
/// App-wide (process-wide) PostSnip settings. Mirrors the video editor's settings service: the
/// dev/app config knob (<see cref="LaunchPostSnipWithFilePath"/>) is read once from
/// appsettings.json, while user preferences live on the observable <see cref="Current"/>, loaded
/// from <c>%LOCALAPPDATA%\Flicksy\post-snip.json</c> and auto-saved on change. Registered as a
/// Singleton.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Optional dev-launch media path from <c>LaunchPostSnipWithFilePath</c> in appsettings.json —
    /// a fallback source when PostSnip is started without a media argument (so it can be run
    /// directly, not only through Snipper). Null when unset.
    /// </summary>
    string? LaunchPostSnipWithFilePath { get; }

    /// <summary>The live, observable user preferences — bind UI to it; mutations auto-save.</summary>
    PostSnipSettings Current { get; }
}
