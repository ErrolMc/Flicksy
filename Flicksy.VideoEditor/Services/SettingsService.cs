using Flicksy.Drawing.Settings;

namespace Flicksy.VideoEditor.Services;

/// <summary>
/// <see cref="ISettingsService"/> backed by a JSON file under <c>%LOCALAPPDATA%\Flicksy\</c>
/// (via <see cref="UserSettingsStore"/>). <see cref="Current"/> is loaded once at construction;
/// every property change on it is written straight back through, so the file always reflects
/// the latest choice. Behavioural settings (e.g. decode mode) are still *applied* at startup —
/// persisting live doesn't mean hot-reloading.
/// </summary>
internal sealed class SettingsService : ISettingsService
{
    private const string FileName = "video-editor.json";

    public VideoEditorSettings Current { get; }

    public bool HardwareDecodeAppliedAtStartup { get; }

    public SettingsService()
    {
        Current = UserSettingsStore.Load<VideoEditorSettings>(FileName);
        // Snapshot the decode mode the app boots with (App.OnStartup pushes exactly this into the
        // decoder), so the Settings overlay knows when a change still needs a restart to apply.
        HardwareDecodeAppliedAtStartup = Current.UseHardwareDecode;
        // Subscribe after load so deserialization populating the object can't trigger a save.
        Current.PropertyChanged += (_, _) => UserSettingsStore.Save(FileName, Current);
    }
}
