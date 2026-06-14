using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Controls.Timeline;

/// <summary>
/// Left-side header for one track. <c>DataContext</c> is a <see cref="Project.Track"/>.
/// Shows the track name and Mute/Lock/Disable toggles bound to the matching <c>Track</c>
/// flags. The Mute (M) button is collapsed on non-Audio kinds via a style trigger.
/// <para>
/// The name is inline-renameable (double-click it, or the right-click "Rename" item): both call
/// <see cref="Track.BeginRename"/>, which swaps the name TextBlock for a TextBox bound to
/// <see cref="Track.EditingName"/>. Commit/cancel routing mirrors <c>ClipView</c> — Enter / focus-out
/// commit, Esc cancels, and a window-level click capture handles clicks on the (non-focusable) timeline.
/// </para>
/// <para>
/// The right-click menu also offers "Delete track". The confirm-if-non-empty prompt lives here in the
/// View (so <see cref="TimelineViewModel.RemoveTrack"/> stays a headless-testable pure mutation); the
/// host VM is reached by walking the visual tree, mirroring <c>ClipView</c>.
/// </para>
/// </summary>
public partial class TrackHeaderView : UserControl
{
    // Window-level click capture during rename, mirroring ClipView: most of the timeline is
    // non-focusable, so LostFocus alone doesn't catch a click on empty lane space. While the rename
    // TextBox is open we listen on the window and commit when a click lands outside this header.
    private Window? _renameWindow;
    private Track? _renameTrack;

    public TrackHeaderView()
    {
        InitializeComponent();
    }

    // ----- Delete -----

    // Delete the track this header represents. Confirms only when the track has clips on it (the user
    // asked for a prompt "if there's anything on that track"); empty tracks delete silently. Allowed on
    // locked tracks too — lock guards clip edits, not the track itself.
    private void OnDeleteTrackClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Track track)
            return;

        TimelineViewModel? timeline = FindTimelineViewModel();
        if (timeline is null)
            return;

        if (track.Clips.Count > 0)
        {
            MessageBoxResult result = MessageBox.Show(
                $"\"{track.Name}\" contains {track.Clips.Count} clip(s).\n\nDelete the track and everything on it?",
                "Delete track",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        timeline.RemoveTrack(track);
    }

    // Walk the visual tree to the host TimelineViewModel (the timeline surface's DataContext),
    // mirroring ClipView.FindTimelineViewModel — this header's own DataContext is the Track.
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

    // ----- Move up / down -----

    // Set the enabled state of the two "Move track up/down" items each time the menu opens. A track can
    // only swap with a same-kind neighbour, so an item greys out at the top / bottom of its kind's group
    // (CanMoveTrackUp / CanMoveTrackDown). Computed here rather than bound because the ContextMenu lives
    // in its own popup tree, away from the TimelineViewModel the Can* checks need (the same reason Delete
    // is wired via code-behind). A null timeline leaves the XAML defaults (enabled) — the click handlers
    // then no-op, and the menu only opens once the view is in the tree anyway.
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not Track track)
            return;

        TimelineViewModel? timeline = FindTimelineViewModel();
        if (timeline is null)
            return;

        MoveTrackUpMenuItem.IsEnabled = timeline.CanMoveTrackUp(track);
        MoveTrackDownMenuItem.IsEnabled = timeline.CanMoveTrackDown(track);
    }

    private void OnMoveTrackUpClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is Track track)
        {
            FindTimelineViewModel()?.MoveTrackUp(track);
        }
    }

    private void OnMoveTrackDownClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is Track track)
        {
            FindTimelineViewModel()?.MoveTrackDown(track);
        }
    }

    // ----- Rename -----

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is Track track)
        {
            track.BeginRename();
        }
    }

    // Double-click the track name to rename — the standard NLE affordance, in addition to the menu item.
    private void OnNameMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && DataContext is Track track)
        {
            track.BeginRename();
            e.Handled = true;
        }
    }

    private void OnRenameTextBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox tb)
            return;

        if (e.NewValue is true)
        {
            // Posting via Dispatcher lets WPF finish the visibility-driven layout pass before we ask
            // for focus — an inline Focus() can be no-op'd otherwise.
            tb.Dispatcher.BeginInvoke(() =>
            {
                tb.Focus();
                tb.SelectAll();
            });

            if (tb.DataContext is Track track)
            {
                _renameWindow = Window.GetWindow(this);
                _renameTrack = track;
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
        // Walk up from the click target. If this header is in the path, the click landed inside us —
        // leave the rename open (lets the user reposition the caret). Otherwise it's outside → commit.
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, this))
                return;

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        _renameTrack?.CommitRename();
    }

    private void DetachRenameWindowCapture()
    {
        if (_renameWindow is not null)
        {
            _renameWindow.PreviewMouseDown -= OnWindowPreviewMouseDownDuringRename;
            _renameWindow = null;
        }
        _renameTrack = null;
    }

    private void OnRenameTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb)
            return;

        if (tb.DataContext is not Track track)
            return;

        if (e.Key == Key.Enter)
        {
            track.CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            track.CancelRename();
            e.Handled = true;
        }
    }

    private void OnRenameTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb)
            return;

        if (tb.DataContext is not Track track)
            return;

        track.CommitRename();
    }
}
