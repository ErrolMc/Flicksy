using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Controls.Timeline;

/// <summary>
/// Visual for one <see cref="Clip"/> inside a <see cref="ClipsLaneView"/>. The
/// per-subtype look comes from typed DataTemplates in the XAML (one for
/// <see cref="MediaClip"/>, one for <see cref="GraphicsClip"/>) — extend by adding a new
/// DataTemplate keyed by the new <see cref="Clip"/> subtype.
///
/// <see cref="IsSelected"/> is pushed down by the parent <see cref="ClipsLaneView"/> when
/// the host's <see cref="TimelineViewModel.SelectedClip"/> changes; the border color
/// reflects it. Click bubbles up via <see cref="VisualTreeHelper"/> walk to find the host
/// <see cref="TimelineViewModel"/> and writes <see cref="TimelineViewModel.SelectedClip"/>
/// directly (the root VM mirrors back to its own SelectedClip).
/// </summary>
public partial class ClipView : UserControl
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected),
        typeof(bool),
        typeof(ClipView),
        new PropertyMetadata(false, OnIsSelectedChanged));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    // Window-level click capture during rename. LostFocus alone isn't enough — most of
    // the timeline (Canvas, Border, Grid) is non-focusable so clicks there never steal
    // focus from the TextBox. We subscribe to the window's PreviewMouseDown while the
    // TextBox is visible and commit when the click lands outside this ClipView.
    private Window? _renameWindow;
    private MediaClip? _renameClip;

    public ClipView()
    {
        InitializeComponent();
        ApplySelectionVisual();
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ClipView)d).ApplySelectionVisual();
    }

    private void ApplySelectionVisual()
    {
        var selectedBrush = (Brush)Resources["ClipSelectedBrush"];
        var defaultBrush = (Brush)Resources["ClipBorderBrush"];
        OuterBorder.BorderBrush = IsSelected ? selectedBrush : defaultBrush;
        OuterBorder.BorderThickness = new Thickness(IsSelected ? 2 : 1);
    }

    private void OnClipMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not Clip clip) return;

        var timeline = FindTimelineViewModel();
        if (timeline is null) return;

        timeline.SelectedClip = clip;
        e.Handled = true;
    }

    // Right-click selects the clip too, but does NOT mark the event handled so WPF's
    // default ContextMenu pop still fires. Without this the user could open the menu on
    // a non-selected clip, which makes the "selected clip is the operand" mental model
    // inconsistent with Detach audio actually running on the clicked clip.
    private void OnClipRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not Clip clip) return;
        var timeline = FindTimelineViewModel();
        if (timeline is null) return;
        timeline.SelectedClip = clip;
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Visible-but-disabled gating. Rename is enabled for any MediaClip (rename target
        // is the per-clip Name override on MediaClip). Detach audio additionally requires
        // Streams=Both — the spec wants users to discover both items even when they don't
        // apply to the current clip subtype.
        var isMediaClip = DataContext is MediaClip;
        RenameMenuItem.IsEnabled = isMediaClip;
        DetachAudioMenuItem.IsEnabled = DataContext is MediaClip { Streams: ClipStreams.Both };
    }

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MediaClip clip)
        {
            clip.BeginRename();
        }
    }

    private void OnDetachAudioClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MediaClip clip) return;
        var timeline = FindTimelineViewModel();
        timeline?.DetachAudio(clip);
    }

    // The inline rename TextBox lives inside the MediaClip DataTemplate; these three
    // handlers are wired by attribute in ClipView.xaml. They route through MediaClip's
    // BeginRename / CommitRename / CancelRename methods, which guard against the
    // LostFocus-after-Enter double-fire via the !IsEditing early return.

    private void OnRenameTextBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        if (e.NewValue is true)
        {
            // Posting via Dispatcher gives WPF time to finish the visibility-driven
            // layout pass before we ask for focus — an inline Focus() can be no-op'd
            // otherwise.
            tb.Dispatcher.BeginInvoke(() =>
            {
                tb.Focus();
                tb.SelectAll();
            });

            // Attach the window-level click capture for the duration of this rename.
            if (tb.DataContext is MediaClip clip)
            {
                _renameWindow = Window.GetWindow(this);
                _renameClip = clip;
                if (_renameWindow is not null)
                {
                    _renameWindow.PreviewMouseDown += OnWindowPreviewMouseDownDuringRename;
                }
            }
        }
        else
        {
            DetachRenameWindowCapture();
        }
    }

    private void OnWindowPreviewMouseDownDuringRename(object sender, MouseButtonEventArgs e)
    {
        // Walk up from the click target. If we find this ClipView in the path, the click
        // landed inside us — leave the rename open (lets the user reposition the caret
        // by clicking inside the TextBox, or interact with their own clip body). Walk
        // past the root without finding ourselves → click is outside → commit.
        var current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, this)) return;
            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        _renameClip?.CommitRename();
    }

    private void DetachRenameWindowCapture()
    {
        if (_renameWindow is not null)
        {
            _renameWindow.PreviewMouseDown -= OnWindowPreviewMouseDownDuringRename;
            _renameWindow = null;
        }
        _renameClip = null;
    }

    private void OnRenameTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not MediaClip clip) return;

        if (e.Key == Key.Enter)
        {
            clip.CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            clip.CancelRename();
            e.Handled = true;
        }
    }

    private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not MediaClip clip) return;
        clip.CommitRename();
    }

    private TimelineViewModel? FindTimelineViewModel()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.DataContext is TimelineViewModel vm)
            {
                return vm;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
