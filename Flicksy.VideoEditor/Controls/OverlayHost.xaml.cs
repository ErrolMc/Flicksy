using System.Windows.Controls;
using System.Windows.Input;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Controls;

/// <summary>
/// The shell's modal overlay layer: a window-wide dim backdrop with the active overlay's
/// tile centered on top. <c>DataContext</c> is the root VM's <see cref="OverlayHostViewModel"/>
/// (rebound in VideoEditorWindow.xaml); the <c>ContentPresenter</c> templates
/// <see cref="OverlayHostViewModel.CurrentOverlay"/> into a tile via the implicit
/// <c>DataTemplate</c>s in this control's resources. Esc handling lives in the window's
/// modal gate (<c>OnPreviewKeyDown</c>), not here — this control never holds focus.
/// </summary>
public partial class OverlayHost : UserControl
{
    public OverlayHost()
    {
        InitializeComponent();
    }

    // Light-dismiss: a click on the dim layer closes the overlay (unless it was shown
    // with allowLightDismiss=false). Tile clicks never get here — the presenter handles
    // MouseLeftButtonDown below so they don't bubble up to the backdrop.
    private void OnBackdropMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OverlayHostViewModel viewModel)
            viewModel.TryLightDismiss();
    }

    private void OnTileMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
