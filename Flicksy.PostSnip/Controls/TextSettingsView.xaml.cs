using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Flicksy.PostSnip.Controls;

public partial class TextSettingsView : UserControl
{
    public TextSettingsView()
    {
        InitializeComponent();
    }

    private void OnFillSettingsPopupOpened(object sender, System.EventArgs e)
    {
        CenterPopupOnPlacementTarget(sender);
    }

    private void OnOutlineSettingsPopupOpened(object sender, System.EventArgs e)
    {
        CenterPopupOnPlacementTarget(sender);
    }

    private static void CenterPopupOnPlacementTarget(object sender)
    {
        if (sender is not Popup popup || popup.Child is not FrameworkElement child)
        {
            return;
        }

        if (popup.PlacementTarget is not FrameworkElement target)
        {
            return;
        }

        child.UpdateLayout();
        double childWidth = child.ActualWidth > 0 ? child.ActualWidth : child.DesiredSize.Width;
        popup.HorizontalOffset = (target.ActualWidth - childWidth) / 2;
    }
}
