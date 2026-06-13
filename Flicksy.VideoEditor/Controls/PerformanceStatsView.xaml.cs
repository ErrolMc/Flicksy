using System.Windows.Controls;

namespace Flicksy.VideoEditor.Controls;

/// <summary>
/// Preview performance HUD overlay — a small stats tile shown top-left over the preview surface.
/// <c>DataContext</c> is a <c>PerformanceStatsViewModel</c> (exposed as
/// <c>PreviewViewModel.PerformanceStats</c>); the VM drives all values and the visibility flag.
/// No code-behind logic.
/// </summary>
public partial class PerformanceStatsView : UserControl
{
    public PerformanceStatsView()
    {
        InitializeComponent();
    }
}
