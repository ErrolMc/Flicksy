using System.Windows;
using System.Windows.Controls;

namespace Flicksy.VideoEditor.Controls;

/// <summary>
/// Tile content for <see cref="ViewModels.SettingsOverlayViewModel"/>: a placeholder
/// editor-preferences panel. Its single "Show performance stats" checkbox is a no-op for
/// now (two-way bound to the VM's own flag, which nothing reads yet). Shown by
/// <see cref="OverlayHost"/>, which owns the dim backdrop, centering, and light-dismiss;
/// this control renders the tile and its Close button (bound to the VM's <c>CloseCommand</c>).
/// </summary>
public partial class SettingsOverlay : UserControl
{
    public SettingsOverlay()
    {
        InitializeComponent();
    }

    // The host creates this view fresh each time the overlay is shown, so Loaded is the
    // "just appeared" hook: pull keyboard focus onto the Close button so Space/Enter hit
    // the dialog instead of whatever editor control held focus underneath the dim layer.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CloseButton.Focus();
    }
}
