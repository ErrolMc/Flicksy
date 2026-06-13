using System.Windows;
using System.Windows.Controls;

namespace Flicksy.VideoEditor.Controls;

/// <summary>
/// Tile content for <see cref="ViewModels.SettingsOverlayViewModel"/>: the editor-preferences
/// panel. Two-way bound to the app-wide <c>VideoEditorSettings</c> (persisted by the settings
/// service): "Show performance stats" and "Use GPU video decoding". The decode toggle shows a
/// restart-required warning while the selection differs from the mode the app booted with. Shown
/// by <see cref="OverlayHost"/>, which owns the dim backdrop, centering, and light-dismiss; this
/// control renders the tile and its Close button (bound to the VM's <c>CloseCommand</c>).
/// </summary>
public partial class SettingsOverlayView : UserControl
{
    public SettingsOverlayView()
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
