using System.Windows;
using System.Windows.Controls;

namespace Flicksy.VideoEditor.Controls;

/// <summary>
/// Tile content for <see cref="ViewModels.ConfirmDialogViewModel"/>: a reusable yes/no
/// confirmation with a caller-supplied header, body, and button labels. Shown by
/// <see cref="OverlayHost"/>, which owns the dim backdrop, centering, and light-dismiss (backdrop
/// click / Esc act as Cancel); this control renders the tile and its two buttons (bound to the VM's
/// <c>ConfirmCommand</c> / <c>CancelCommand</c>).
/// </summary>
public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView()
    {
        InitializeComponent();
    }

    // The host creates this view fresh each time the overlay is shown, so Loaded is the "just
    // appeared" hook: focus the Cancel button so Space/Enter dismiss rather than confirm (the safe
    // default for a destructive prompt) and so the keys hit the dialog instead of whatever editor
    // control held focus under the dim layer.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CancelButton.Focus();
    }
}
