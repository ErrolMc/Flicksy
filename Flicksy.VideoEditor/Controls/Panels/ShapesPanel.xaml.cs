using System.Windows.Controls;

namespace Flicksy.VideoEditor.Controls.Panels;

/// <summary>
/// The Shapes left-rail panel: shape-kind picker + fill/outline swatches + outline thickness, bound
/// to the shared <c>ShapeSettingsViewModel</c> on <c>GraphicsEditor</c>. Selecting a shape arms the
/// overlay's shape tool (and re-arms after a placement auto-switches to Select); the same VM also
/// restyles the selected shape when one is being edited.
/// </summary>
public partial class ShapesPanel : UserControl
{
    public ShapesPanel()
    {
        InitializeComponent();
    }
}
