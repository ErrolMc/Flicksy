using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Overlay content VM for the editor Settings tile (templated by
/// <see cref="Controls.OverlayHost"/>, view is <see cref="Controls.SettingsOverlay"/>).
/// Placeholder shell for editor-wide preferences. <see cref="ShowPerformanceStats"/> is a
/// no-op toggle for now — the checkbox flips it but nothing reads it yet (the HUD it will
/// drive lands later); it resets each time the overlay opens since this VM is recreated.
/// Close is an injected <see cref="Action"/> (the root VM passes <c>OverlayHost.Close</c>)
/// so content VMs never hold a host reference.
/// </summary>
public partial class SettingsOverlayViewModel : ObservableObject
{
    private readonly Action _close;

    // No-op placeholder option. Wire a consumer in when the performance-stats HUD lands.
    [ObservableProperty]
    private bool showPerformanceStats;

    public SettingsOverlayViewModel(Action close)
    {
        _close = close;
    }

    [RelayCommand]
    private void Close()
    {
        _close();
    }
}
