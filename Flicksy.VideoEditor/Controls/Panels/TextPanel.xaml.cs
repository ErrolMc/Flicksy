using System.Windows.Controls;

namespace Flicksy.VideoEditor.Controls.Panels;

/// <summary>
/// The Text left-rail panel: "Add text box" (arms text placement) + font / size / fill / outline,
/// bound to the shared <c>TextSettingsViewModel</c> on <c>GraphicsEditor</c>. The same VM restyles
/// the selected text when one is being edited.
/// </summary>
public partial class TextPanel : UserControl
{
    public TextPanel()
    {
        InitializeComponent();
    }
}
