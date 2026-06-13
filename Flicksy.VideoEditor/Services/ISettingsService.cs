namespace Flicksy.VideoEditor.Services;

/// <summary>
/// App-wide (process-wide) editor settings, read once at startup from configuration.
/// Registered as a Singleton — there is no per-document or per-window identity here, so a
/// single instance is correct for the whole process. As real user-editable options land
/// (the Settings overlay is a placeholder today) they grow on this interface.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// GPU-decode kill switch (ADR 0010), read from <c>DisableHardwareDecode</c> in
    /// appsettings.json. Pushed into
    /// <see cref="Flicksy.Drawing.Media.HardwareMediaDecoder.Disabled"/> at startup.
    /// </summary>
    bool DisableHardwareDecode { get; }
}
