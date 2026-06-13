using CommunityToolkit.Mvvm.ComponentModel;

namespace Flicksy.PostSnip.Services;

/// <summary>
/// App-wide, user-editable PostSnip preferences, persisted to
/// <c>%LOCALAPPDATA%\Flicksy\post-snip.json</c> by <see cref="ISettingsService"/>. Empty for
/// now — intentional scaffolding so the writable-settings mechanism matches the video editor's
/// exactly (same <see cref="Flicksy.Drawing.Settings.UserSettingsStore"/>, same auto-save). The
/// file is written lazily on the first change, so nothing lands on disk until a real option
/// exists; add observable properties here as user-facing options arrive.
/// </summary>
public partial class PostSnipSettings : ObservableObject
{
}
