using System.Windows;
using System.Windows.Controls;

namespace Flicksy.VideoEditor.Controls;

/// <summary>
/// Tile content for <see cref="ViewModels.ConfirmDialogViewModel"/>: the editor's reusable message
/// tile with a caller-supplied header, body, and button labels — a two-button confirm or a
/// single-button info / error acknowledgement (the Cancel button collapses on <c>HasCancel == false</c>).
/// Shown by <see cref="OverlayHost"/>, which owns the dim backdrop, centering, and light-dismiss
/// (backdrop click / Esc act as Cancel); this control renders the tile and its buttons (bound to the
/// VM's <c>ConfirmCommand</c> / <c>CancelCommand</c>).
/// </summary>
public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView()
    {
        InitializeComponent();
    }

    // The host creates this view fresh each time the overlay is shown, so Loaded is the "just
    // appeared" hook: move focus into the dialog so Space/Enter hit it rather than whatever editor
    // control held focus under the dim layer. Focus Cancel on a confirm (the safe default for a
    // destructive prompt); on a single-button acknowledgement Cancel is collapsed, so focus the lone
    // OK button instead.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (CancelButton.Visibility == Visibility.Visible)
            CancelButton.Focus();
        else
            ConfirmButton.Focus();
    }
}
