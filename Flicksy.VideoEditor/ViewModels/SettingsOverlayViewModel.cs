using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.VideoEditor.Services;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Overlay content VM for the editor Settings tile (templated by
/// <see cref="Controls.OverlayHost"/>, view is <see cref="Controls.SettingsOverlay"/>).
/// Exposes the app-wide <see cref="VideoEditorSettings"/> for two-way binding; because that is
/// the settings service's single live instance, edits persist across reopens and launches (the
/// service auto-saves on change). The decode toggle is proxied through
/// <see cref="UseHardwareDecode"/> so flipping it also refreshes <see cref="DecodeRestartRequired"/>
/// — the "restart to apply" warning, which compares the selection against the decode mode the
/// process actually booted with and clears only when the two match again. Close is an injected
/// <see cref="Action"/> (the root VM passes <c>OverlayHost.Close</c>) so content VMs never hold a
/// host reference.
/// </summary>
public partial class SettingsOverlayViewModel : ObservableObject
{
    private readonly Action _close;

    // The decode mode pushed into the decoder at process startup. A change to UseHardwareDecode
    // only takes effect on the next launch, so the selection differing from this is exactly what
    // the restart warning keys off of.
    private readonly bool _hardwareDecodeAtStartup;

    public VideoEditorSettings Settings { get; }

    public SettingsOverlayViewModel(VideoEditorSettings settings, bool hardwareDecodeAtStartup, Action close)
    {
        Settings = settings;
        _hardwareDecodeAtStartup = hardwareDecodeAtStartup;
        _close = close;
    }

    /// <summary>
    /// Two-way proxy for <see cref="VideoEditorSettings.UseHardwareDecode"/> (the service auto-saves
    /// the write via <c>Settings</c>'s own change notification). Bound by the decode checkbox so a
    /// toggle also refreshes <see cref="DecodeRestartRequired"/>.
    /// </summary>
    public bool UseHardwareDecode
    {
        get => Settings.UseHardwareDecode;
        set
        {
            if (Settings.UseHardwareDecode == value)
                return;

            Settings.UseHardwareDecode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DecodeRestartRequired));
        }
    }

    /// <summary>
    /// True when the selected decode mode no longer matches the one the process booted with, so a
    /// restart is needed to apply it. Drives the warning's visibility; clears when the user sets the
    /// toggle back.
    /// </summary>
    public bool DecodeRestartRequired => Settings.UseHardwareDecode != _hardwareDecodeAtStartup;

    [RelayCommand]
    private void Close()
    {
        _close();
    }
}
