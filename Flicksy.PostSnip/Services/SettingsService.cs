using Flicksy.Drawing.Settings;
using Microsoft.Extensions.Configuration;

namespace Flicksy.PostSnip.Services;

/// <summary>
/// <see cref="ISettingsService"/> with two backings, mirroring the video editor: the dev-launch
/// knob is read once from the host's <see cref="IConfiguration"/> (appsettings.json), while user
/// preferences load from / save to a JSON file under <c>%LOCALAPPDATA%\Flicksy\</c> (via
/// <see cref="UserSettingsStore"/>). <see cref="Current"/> auto-persists every change.
/// </summary>
internal sealed class SettingsService : ISettingsService
{
    private const string FileName = "post-snip.json";

    public string? LaunchPostSnipWithFilePath { get; }

    public PostSnipSettings Current { get; }

    public SettingsService(IConfiguration configuration)
    {
        LaunchPostSnipWithFilePath = configuration["LaunchPostSnipWithFilePath"];
        Current = UserSettingsStore.Load<PostSnipSettings>(FileName);
        // Subscribe after load so deserialization populating the object can't trigger a save.
        Current.PropertyChanged += (_, _) => UserSettingsStore.Save(FileName, Current);
    }
}
