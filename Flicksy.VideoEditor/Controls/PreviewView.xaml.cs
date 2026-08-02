using System;
using System.Windows;
using System.Windows.Controls;

namespace Flicksy.VideoEditor.Controls;

/// <summary>
/// Top-of-center-column preview surface. The <see cref="Image"/>'s <c>Stretch=Uniform</c>
/// combined with a <c>WriteableBitmap</c> source sized to the project resolution
/// (owned by <c>PreviewViewModel</c> and painted in place each frame by
/// <c>SkiaCompositor.RenderFrame</c>, surfaced as <c>PreviewViewModel.CurrentFrame</c>)
/// letterboxes the content against the control's dark background.
/// <para>
/// The only code-behind is <see cref="OnPreviewSizeChanged"/>, which keeps the graphics-editing
/// overlay (a project-resolution <c>DrawingView</c>) aligned with that letterbox: it scales the
/// overlay by the same min-fit factor and centers it, so a stroke drawn at a project pixel lands
/// exactly where the compositor would paint it (ADR 0013). Everything else is binding-driven.
/// </para>
/// </summary>
public partial class PreviewView : UserControl
{
    public PreviewView()
    {
        InitializeComponent();
    }

    // Fires on both the preview viewport resizing (PreviewRoot) and the project resolution changing
    // (OverlayContent's bound Width/Height). Reproduces the Image's Stretch=Uniform fit: scale =
    // min(viewport/project) on each axis, then center the scaled overlay in the leftover letterbox.
    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double projectWidth = OverlayContent.Width;
        double projectHeight = OverlayContent.Height;
        double viewportWidth = PreviewRoot.ActualWidth;
        double viewportHeight = PreviewRoot.ActualHeight;

        if (projectWidth <= 0 || projectHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0
            || double.IsNaN(projectWidth) || double.IsNaN(projectHeight))
        {
            return;
        }

        double scale = Math.Min(viewportWidth / projectWidth, viewportHeight / projectHeight);
        OverlayScale.ScaleX = scale;
        OverlayScale.ScaleY = scale;
        OverlayTranslate.X = (viewportWidth - (projectWidth * scale)) / 2.0;
        OverlayTranslate.Y = (viewportHeight - (projectHeight * scale)) / 2.0;
    }
}
