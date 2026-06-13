namespace Flicksy.PostSnip.Services;

/// <summary>
/// App-wide (process-wide) PostSnip settings, read once at startup from configuration. Mirrors
/// the video editor's settings service (same config-behind-a-service pattern); registered as a
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
}
