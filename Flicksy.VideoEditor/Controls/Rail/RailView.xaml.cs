using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Controls.Rail;

/// <summary>
/// Vertical strip of icon buttons backing the left and right editor rails. Single
/// selection via <see cref="SelectedTag"/>; clicking the already-selected item toggles
/// <see cref="IsPanelOpen"/> instead of re-selecting (the "click active tab to collapse"
/// pattern). <see cref="ItemsEnabled"/> gates all tab buttons together — used by the
/// right rail to disable inspectors when no clip is selected.
/// </summary>
public partial class RailView : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(RailView),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedTagProperty = DependencyProperty.Register(
        nameof(SelectedTag),
        typeof(object),
        typeof(RailView),
        new FrameworkPropertyMetadata(
            defaultValue: null,
            flags: FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            propertyChangedCallback: OnSelectedTagChanged));

    public static readonly DependencyProperty IsPanelOpenProperty = DependencyProperty.Register(
        nameof(IsPanelOpen),
        typeof(bool),
        typeof(RailView),
        new FrameworkPropertyMetadata(
            defaultValue: false,
            flags: FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ItemsEnabledProperty = DependencyProperty.Register(
        nameof(ItemsEnabled),
        typeof(bool),
        typeof(RailView),
        new PropertyMetadata(true));

    private bool _syncingSelection;

    public RailView()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedTag
    {
        get => GetValue(SelectedTagProperty);
        set => SetValue(SelectedTagProperty, value);
    }

    public bool IsPanelOpen
    {
        get => (bool)GetValue(IsPanelOpenProperty);
        set => SetValue(IsPanelOpenProperty, value);
    }

    public bool ItemsEnabled
    {
        get => (bool)GetValue(ItemsEnabledProperty);
        set => SetValue(ItemsEnabledProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RailView rv)
        {
            rv.SyncListBoxSelectionFromTag();
        }
    }

    private static void OnSelectedTagChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RailView rv)
        {
            rv.SyncListBoxSelectionFromTag();
        }
    }

    private void SyncListBoxSelectionFromTag()
    {
        if (_syncingSelection) 
            return;

        if (ItemsSource is null) 
            return;

        RailItem? match = ItemsSource.OfType<RailItem>().FirstOrDefault(i => Equals(i.Tag, SelectedTag));

        _syncingSelection = true;
        try
        {
            RailList.SelectedItem = match;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnPreviewLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // ListBoxItem.Focusable=False (set in the rail's style so the icon buttons don't
        // show a focus rect) makes ListBox's built-in click-to-select a no-op — its
        // internal HandleMouseButtonDown bails when Focus() fails. So we handle the
        // click ourselves: same-item toggles IsPanelOpen, different-item changes
        // selection and opens.
        if (e.OriginalSource is not DependencyObject source) 
            return;

        ListBoxItem? container = ItemsControl.ContainerFromElement(RailList, source) as ListBoxItem;
        if (container is null) 
            return;

        if (!container.IsEnabled) 
            return;

        if (container.DataContext is not RailItem item) 
            return;

        if (Equals(SelectedTag, item.Tag))
        {
            IsPanelOpen = !IsPanelOpen;
        }
        else
        {
            SelectedTag = item.Tag;
            IsPanelOpen = true;
        }
        e.Handled = true;
    }
}
