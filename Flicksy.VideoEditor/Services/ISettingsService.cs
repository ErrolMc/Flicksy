namespace Flicksy.VideoEditor.Services;

/// <summary>
/// App-wide (process-wide) editor settings. <see cref="Current"/> is loaded once at startup
/// from <c>%LOCALAPPDATA%\Flicksy\video-editor.json</c> and persisted automatically whenever a
/// property on it changes. Registered as a Singleton — there is no per-document or per-window
/// identity here, so one instance is correct for the whole process. New user options grow on
/// <see cref="VideoEditorSettings"/>.
/// </summary>
public interface ISettingsService
{
    /// <summary>The live, observable settings object — bind UI to it; mutations auto-save.</summary>
    VideoEditorSettings Current { get; }

    /// <summary>
    /// <see cref="VideoEditorSettings.UseHardwareDecode"/> as captured at process startup — the
    /// value pushed into the decoder. The Settings overlay compares the live selection against this
    /// to know when a restart is required to apply a decode change.
    /// </summary>
    bool HardwareDecodeAppliedAtStartup { get; }
}
