using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;
using Flicksy.PostSnip.ViewModels;

namespace Flicksy.PostSnip.Controls;

public partial class ImageEditToolsView : UserControl
{
    public ImageEditToolsView()
    {
        InitializeComponent();
    }

    private void OnPenSettingsPopupOpened(object sender, System.EventArgs e)
    {
        // Begin a style-edit session if a pen stroke is selected — color picks and size drags
        // inside the popup mutate the stroke live, but no undo entry is pushed until the popup
        // closes (see OnPenSettingsPopupClosed). Mirrors the shape/text-settings flow. The pen
        // popup keeps its own fixed placement offset, so this does not re-center it.
        if (TryGetSelectedPenItem(out var drawing, out var penItem))
        {
            // Sync the popup's size slider / swatch to the selected stroke first so the user sees
            // its actual style. Done BEFORE BeginPenStyleEdit so the snapshot captures the stroke's
            // real state (the sync writes the same values back via the existing cascade —
            // PenStrokeItem's SetProperty guards short-circuit no-op writes).
            if (DataContext is ImageEditToolsViewModel tools)
            {
                tools.PenSettings.SyncFromPenStrokeItem(penItem);
            }
            drawing.BeginPenStyleEdit(penItem);
        }
    }

    private void OnPenSettingsPopupClosed(object sender, System.EventArgs e)
    {
        if (TryGetDrawingViewModel(out var drawing))
        {
            drawing.EndPenStyleEdit();
        }
    }

    private void OnShapeSettingsPopupOpened(object sender, System.EventArgs e)
    {
        CenterPopupOnPlacementTarget(sender);

        // Begin a style-edit session if a shape item is selected — slider drags and color
        // picks inside the popup mutate the item live, but no undo entry is pushed until the
        // popup closes (see OnShapeSettingsPopupClosed). Mirrors the text-settings flow.
        if (TryGetSelectedShapeItem(out var drawing, out var shapeItem))
        {
            // Sync the popup's sliders / swatches to the selected shape first so the user sees
            // its actual style. Done BEFORE BeginShapeStyleEdit so the snapshot captures the
            // item's real state (the sync writes the same values back via the existing
            // cascade — ShapeItem's SetProperty guards short-circuit no-op writes).
            if (DataContext is ImageEditToolsViewModel tools)
            {
                tools.ShapeSettings.SyncFromShapeItem(shapeItem);
            }
            drawing.BeginShapeStyleEdit(shapeItem);
        }
    }

    private void OnShapeSettingsPopupClosed(object sender, System.EventArgs e)
    {
        if (TryGetDrawingViewModel(out var drawing))
        {
            drawing.EndShapeStyleEdit();
        }
    }

    private void OnTextSettingsPopupOpened(object sender, System.EventArgs e)
    {
        CenterPopupOnPlacementTarget(sender);

        // Begin a style-edit session if a text item is selected — slider drags and color
        // picks inside the popup mutate the item live, but no undo entry is pushed until
        // the popup closes (see OnTextSettingsPopupClosed).
        if (TryGetSelectedTextItem(out var drawing, out var textItem))
        {
            // First, sync the popup's sliders / swatches to match the selected item so the
            // user sees its actual style — not stale leftover values from the previous text.
            // Done BEFORE BeginTextStyleEdit so the snapshot captures the item's real state
            // (the sync writes the same values back via the existing cascade — TextItem's
            // SetProperty guards short-circuit no-op writes).
            if (DataContext is ImageEditToolsViewModel tools)
            {
                tools.TextSettings.SyncFromTextItem(textItem);
            }
            drawing.BeginTextStyleEdit(textItem);
        }
    }

    private void OnTextSettingsPopupClosed(object sender, System.EventArgs e)
    {
        if (TryGetDrawingViewModel(out var drawing))
        {
            drawing.EndTextStyleEdit();
        }
    }

    private bool TryGetDrawingViewModel(out DrawingViewModel drawing)
    {
        drawing = default!;
        if (Window.GetWindow(this)?.DataContext is not PostSnipViewModel post)
        {
            return false;
        }
        drawing = post.Drawing;
        return true;
    }

    private bool TryGetSelectedTextItem(out DrawingViewModel drawing, out TextItem textItem)
    {
        textItem = default!;
        if (!TryGetDrawingViewModel(out drawing))
        {
            return false;
        }
        if (drawing.SelectedItem is not TextItem t)
        {
            return false;
        }
        textItem = t;
        return true;
    }

    private bool TryGetSelectedPenItem(out DrawingViewModel drawing, out PenStrokeItem penItem)
    {
        penItem = default!;
        if (!TryGetDrawingViewModel(out drawing))
        {
            return false;
        }
        if (drawing.SelectedItem is not PenStrokeItem p)
        {
            return false;
        }
        penItem = p;
        return true;
    }

    private bool TryGetSelectedShapeItem(out DrawingViewModel drawing, out ShapeItem shapeItem)
    {
        shapeItem = default!;
        if (!TryGetDrawingViewModel(out drawing))
        {
            return false;
        }
        if (drawing.SelectedItem is not ShapeItem s)
        {
            return false;
        }
        shapeItem = s;
        return true;
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
