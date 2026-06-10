using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Controls.Panels;

/// <summary>
/// Media bin panel — the Media tab's left-panel content. Replaces the
/// <c>StubSurface</c> placeholder. Hosts a toolbar (Import) plus a
/// <see cref="WrapPanel"/> of bin cells over <see cref="MediaBinViewModel.MediaSources"/>,
/// with an empty-state hint when the project has no imported sources. Files dropped from
/// Windows Explorer onto the panel surface route through the same
/// <see cref="MediaBinViewModel.TryImportFiles"/> path so dedupe + probe-failure handling
/// stay uniform with the Import button. Right-click on a cell offers Rename,
/// Relocate… (enabled only when the source is missing), Reveal in Explorer, and Remove
/// (with a cascade-delete confirm if any timeline clips reference the source). Missing
/// sources are signaled by a red cell border, red name text, and a "?" placeholder in
/// the thumbnail area. Inline rename uses an in-cell TextBox; this file owns its
/// commit/cancel/lost-focus handlers.
/// </summary>
public partial class MediaPanel : UserControl
{
    private Point _dragOriginScreen;
    private MediaSourceViewModel? _pendingDragEntry;

    // Pixel height of the details row stashed across collapse so re-expanding restores
    // the user's drag-resized size (mirrors VideoEditorWindow's left-panel width stash).
    private GridLength _stashedDetailsHeight = GridLength.Auto;

    public MediaPanel()
    {
        InitializeComponent();
    }

    private void OnPanelDragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsAcceptedFileDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPanelDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MediaBinViewModel vm) 
            return;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) 
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            vm.TryImportFiles(paths);
        }
        e.Handled = true;
    }

    private static bool IsAcceptedFileDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) 
            return false;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) 
            return false;

        foreach (string path in paths)
        {
            if (MediaBinViewModel.IsAcceptedMediaPath(path)) 
                return true;
        }
        return false;
    }

    // Click anywhere inside the panel that isn't a cell clears the bin selection
    // (toolbar, empty grid area, scrollbar, padding around cells). Clicks on a cell
    // are also where bin-to-timeline drag originates — record the cell + screen point
    // and let OnPanelPreviewMouseMove decide whether the cursor has moved past the
    // system drag threshold before kicking off DoDragDrop. Missing sources skip drag
    // initiation (no payload — the drop matrix would refuse them anyway).
    private void OnPanelPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MediaBinViewModel vm) 
            return;

        if (e.OriginalSource is not DependencyObject d) 
            return;

        ListBoxItem? item = FindAncestor<ListBoxItem>(d);
        if (item is null)
        {
            // Clicks on the details pane (header toggle, metadata rows, resize splitter)
            // are interactions with the current selection — they must not clear it.
            if (FindAncestor<Expander>(d) == DetailsExpander || FindAncestor<GridSplitter>(d) == DetailsSplitter)
                return;

            if (vm.SelectedSource is not null)
                vm.SelectedSource = null;
            return;
        }

        if (item.DataContext is MediaSourceViewModel entry && !entry.Source.IsMissing)
        {
            _pendingDragEntry = entry;
            _dragOriginScreen = e.GetPosition(this);
        }
    }

    private void OnPanelPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pendingDragEntry is null) 
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pendingDragEntry = null;
            return;
        }

        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragOriginScreen.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragOriginScreen.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        MediaSourceViewModel entry = _pendingDragEntry;
        _pendingDragEntry = null;
        StartDrag(entry);
    }

    private void OnPanelMouseLeave(object sender, MouseEventArgs e)
    {
        _pendingDragEntry = null;
    }

    // The details pane's row heights are code-behind-managed because the GridSplitter
    // writes heights directly (a binding would be clobbered on the first drag) — same
    // reasoning as VideoEditorWindow's panel columns.

    // The splitter band is visible in both states for a consistent separator, but only
    // resizes the expanded pane — a drag while collapsed would stretch the header-only
    // row into dead space, so it's cancelled before it moves anything.
    private void OnDetailsSplitterDragStarted(object sender, DragStartedEventArgs e)
    {
        if (!DetailsExpander.IsExpanded)
            DetailsSplitter.CancelDrag();
    }

    // The splitter converts BodyRow and DetailsRow to pixel heights while dragging.
    // Give the body its star back afterward so panel-height changes flex the bin list
    // while the details pane keeps the user's chosen height.
    private void OnDetailsSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        BodyRow.Height = new GridLength(1, GridUnitType.Star);
    }

    private void OnDetailsExpanded(object sender, RoutedEventArgs e)
    {
        DetailsRow.MinHeight = 90;
        if (_stashedDetailsHeight.IsAbsolute)
            DetailsRow.Height = _stashedDetailsHeight;
    }

    // A drag-set pixel height must not survive collapse (the header-only row would keep
    // the expanded height as dead space) — stash it for the next expand and return the
    // row to Auto so it hugs the header. MinHeight likewise only applies while expanded.
    private void OnDetailsCollapsed(object sender, RoutedEventArgs e)
    {
        if (DetailsRow.Height.IsAbsolute)
            _stashedDetailsHeight = DetailsRow.Height;

        DetailsRow.Height = GridLength.Auto;
        DetailsRow.MinHeight = 0;
    }

    // DoDragDrop blocks the message loop until the drag completes. The cursor adorner
    // is added to the window's AdornerLayer before the call so it survives the panel-to-
    // timeline boundary, and PreviewDragOver on the window pumps cursor updates into it.
    // The finally block guarantees the adorner + handler come off even if the drag is
    // cancelled mid-flight (Esc, source removal, etc.).
    private void StartDrag(MediaSourceViewModel entry)
    {
        Window window = Window.GetWindow(this);
        UIElement? content = window?.Content as UIElement;
        AdornerLayer? layer = content is not null ? AdornerLayer.GetAdornerLayer(content) : null;

        DragThumbnailAdorner? adorner = null;
        DragEventHandler? onPreviewDragOver = null;
        QueryContinueDragEventHandler? onQueryContinue = null;

        if (layer is not null && content is not null && window is not null)
        {
            adorner = new DragThumbnailAdorner(content, entry.Thumbnail);
            layer.Add(adorner);

            onPreviewDragOver = (_, ev) => adorner.UpdatePosition(ev.GetPosition(content));
            window.PreviewDragOver += onPreviewDragOver;

            // GiveFeedback only fires on the drag source — QueryContinueDrag fires on
            // the source during the whole drag and gives us a heartbeat for the cursor
            // position even when the cursor leaves the timeline (over the title bar,
            // outside the window, etc.).
            onQueryContinue = (_, ev) =>
            {
                if (Mouse.PrimaryDevice.DirectlyOver is IInputElement el)
                {
                    Point p = Mouse.GetPosition(content);
                    adorner.UpdatePosition(p);
                }
            };
            AddHandler(QueryContinueDragEvent, onQueryContinue, handledEventsToo: true);
        }

        var data = new DataObject(typeof(MediaSource), entry.Source);
        try
        {
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }
        finally
        {
            if (adorner is not null && layer is not null)
            {
                layer.Remove(adorner);
            }
            if (window is not null && onPreviewDragOver is not null)
            {
                window.PreviewDragOver -= onPreviewDragOver;
            }
            if (onQueryContinue is not null)
            {
                RemoveHandler(QueryContinueDragEvent, onQueryContinue);
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        DependencyObject current = start;
        while (current is not null)
        {
            if (current is T match) 
                return match;

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    // The inline rename TextBox lives inside the cell DataTemplate; these three handlers
    // are wired by attribute in MediaPanel.xaml. They route through the bin VM's
    // BeginRename / CommitRename / CancelRename commands — which guard against double-fire
    // (Enter commits → TextBox collapses → LostFocus fires CommitRename again → IsEditing
    // is already false → early-return).

    private void OnRenameTextBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox tb) 
            return;

        if (e.NewValue is not true) 
            return;

        // Posting via Dispatcher is more reliable than calling Focus() inline — the
        // TextBox is mid-template-instantiation when IsVisibleChanged fires, and an
        // immediate Focus() call can be no-op'd by WPF still finishing the layout pass.
        tb.Dispatcher.BeginInvoke(() =>
        {
            tb.Focus();
            tb.SelectAll();
        });
    }

    private void OnRenameTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) 
            return;

        if (tb.DataContext is not MediaSourceViewModel entry) 
            return;

        if (DataContext is not MediaBinViewModel vm) 
            return;

        if (e.Key == Key.Enter)
        {
            vm.CommitRenameCommand.Execute(entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelRenameCommand.Execute(entry);
            e.Handled = true;
        }
    }

    private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) 
            return;

        if (tb.DataContext is not MediaSourceViewModel entry) 
            return;

        if (DataContext is not MediaBinViewModel vm) 
            return;

        vm.CommitRenameCommand.Execute(entry);
    }
}

/// <summary>
/// Cursor-attached drag preview painted on the window's <see cref="AdornerLayer"/>. Drawn
/// at 50% opacity and ~80 px wide preserving the source thumbnail's aspect — the lane's
/// own ghost rect (see <see cref="Timeline.GhostClipAdorner"/>) tells the user where the
/// clip would land, while this one keeps the dragged identity attached to the cursor as
/// it crosses the panel/timeline boundary. Audio sources (and any source without a cached
/// thumbnail) render as a small placeholder rect so the cursor still has a follower.
/// </summary>
internal sealed class DragThumbnailAdorner : Adorner
{
    private const double TargetWidth = 80;
    private const double PlaceholderHeight = 60;
    private static readonly Brush PlaceholderFill = CreateFrozenBrush(Color.FromArgb(0x80, 0x55, 0x55, 0x55));
    private static readonly Vector CursorOffset = new(14, 14);

    private readonly ImageSource? _thumbnail;
    private Point _position;

    public DragThumbnailAdorner(UIElement adornedElement, ImageSource? thumbnail) : base(adornedElement)
    {
        _thumbnail = thumbnail;
        IsHitTestVisible = false;
    }

    public void UpdatePosition(Point position)
    {
        _position = position;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        Point origin = _position + CursorOffset;
        double width = TargetWidth;
        double height = PlaceholderHeight;
        if (_thumbnail is not null && _thumbnail.Width > 0 && _thumbnail.Height > 0)
        {
            height = width * _thumbnail.Height / _thumbnail.Width;
        }
        var rect = new Rect(origin.X, origin.Y, width, height);
        dc.PushOpacity(0.5);
        if (_thumbnail is not null)
        {
            dc.DrawImage(_thumbnail, rect);
        }
        else
        {
            dc.DrawRectangle(PlaceholderFill, null, rect);
        }
        dc.Pop();
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
