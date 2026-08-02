using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Windows;

public partial class VideoEditorWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const double DefaultPanelWidth = 280;
    private const double LeftRailWidth = 44;
    private const double CenterMinWidth = 320;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // Remembers the left panel's last user-resized width so re-expanding restores
    // it rather than snapping back to the default. Set when the panel is open and
    // the user drags the splitter (or initially from the XAML's 280 default). The
    // right panel has no splitter, so it always toggles 0 ↔ DefaultPanelWidth.
    private double _lastLeftPanelWidth = DefaultPanelWidth;
    private const double RightRailWidth = 44;

    public VideoEditorWindow()
        : this(viewModel: new VideoEditorViewModel(Project.Project.CreateEmpty()), sourcePath: null)
    {
    }

    public VideoEditorWindow(string? sourcePath)
        : this(viewModel: new VideoEditorViewModel(Project.Project.CreateEmpty()), sourcePath: sourcePath)
    {
    }

    public VideoEditorWindow(VideoEditorViewModel viewModel, string? sourcePath = null)
    {
        InitializeComponent();

        ViewModel = viewModel;
        DataContext = viewModel;
        SourcePath = sourcePath;

        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            Title = $"Flicksy Video Editor — {Path.GetFileName(sourcePath)}";
        }

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncPanelColumnsFromViewModel();
    }

    public VideoEditorViewModel ViewModel { get; }

    public string? SourcePath { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) 
            return;

        int useDark = 1;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, sizeof(int));
        }
    }

    // Forward window activations to the bin VM so it can re-check every imported source
    // against the filesystem and flip IsMissing on/off. Fires on every alt-tab back into
    // the window (including the first show — harmless on an empty bin), so a file
    // renamed/moved behind the editor's back lights up red as soon as focus returns.
    // See MediaBinViewModel.RefreshMissingState for the scan details.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        ViewModel.MediaBin.RefreshMissingState();
    }

    // Tear down compositor + decoder cache when the window closes. The VM owns the
    // compositor; window close is the natural disposal point per WPF lifetime semantics.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ViewModel.Dispose();
    }

    // Window-level NLE shortcuts. All are ignored while a text box has focus (inline clip/bin
    // rename needs the keys for typing) and on auto-repeat. Handled here
    // (PreviewKeyDown) so Space overrides Space-activates-button on the transport buttons.
    //   Space            — play/pause (any modifier, matching the prior behavior).
    //   S / Delete / C   — split selection at playhead / delete selection / toggle razor mode.
    // The edit keys are bare-key only so Ctrl/Alt chords (Ctrl+Z undo, a future Ctrl+S save) pass
    // through. Esc-cancel + razor-exit live on TimelineView's window hook (it owns the tool router);
    // marking Esc handled in the modal gate below runs first (class handler before instance
    // handlers), so closing the overlay wins over razor-exit.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.IsRepeat)
            return;

        // Modal gate: while any overlay (Project Settings, Settings, …) is up, Esc
        // light-dismisses it and every editor shortcut below is suppressed (the dim layer
        // blocks the mouse). Esc is swallowed even when the overlay refuses light dismissal
        // so it can't leak through to the timeline's razor-exit hook.
        if (ViewModel.OverlayHost.IsOverlayOpen)
        {
            if (e.Key == Key.Escape)
            {
                ViewModel.OverlayHost.TryLightDismiss();
                e.Handled = true;
            }
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase)
            return;

        if (e.Key == Key.Space)
        {
            ViewModel.Transport.PlayPauseCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.S:
                ViewModel.Timeline.SplitSelectedAtPlayheadCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete:
                ViewModel.Timeline.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.C:
                ViewModel.Timeline.IsRazorMode = !ViewModel.Timeline.IsRazorMode;
                e.Handled = true;
                break;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(VideoEditorViewModel.IsLeftPanelOpen):
                ApplyLeftPanelState();
                break;
            case nameof(VideoEditorViewModel.IsRightPanelOpen):
                ApplyRightPanelState();
                break;
            case nameof(VideoEditorViewModel.SelectedClip):
                ApplyRightRailState();
                break;
        }
    }

    private void SyncPanelColumnsFromViewModel()
    {
        ApplyLeftPanelState();
        ApplyRightPanelState();
        ApplyRightRailState();
    }

    private void ApplyLeftPanelState()
    {
        if (ViewModel.IsLeftPanelOpen)
        {
            LeftPanelColumn.Width = new GridLength(_lastLeftPanelWidth);
        }
        else
        {
            // Remember the width we're collapsing from (might be the user-dragged value).
            if (LeftPanelColumn.Width.IsAbsolute && LeftPanelColumn.Width.Value > 0)
            {
                _lastLeftPanelWidth = LeftPanelColumn.Width.Value;
            }
            LeftPanelColumn.Width = new GridLength(0);
        }
        UpdateLeftPanelMaxWidth();
    }

    private void ApplyRightPanelState()
    {
        RightPanelColumn.Width = ViewModel.IsRightPanelOpen
            ? new GridLength(DefaultPanelWidth)
            : new GridLength(0);
        UpdateLeftPanelMaxWidth();
    }

    private void ApplyRightRailState()
    {
        if (ViewModel.SelectedClip is Project.MediaClip)
        {
            RightRailColumn.Width = new GridLength(RightRailWidth);
        }
        else
        {
            // No MediaClip selected → the per-clip inspectors (Speed/Audio/…) aren't meaningful, so
            // hide the rail entirely and force any open inspector closed. A GraphicsClip's style is
            // edited from the left Shapes/Text panels; its transform inspector is #15.
            RightRailColumn.Width = new GridLength(0);
            ViewModel.IsRightPanelOpen = false;
        }
        UpdateLeftPanelMaxWidth();
    }

    // Caps the left panel so dragging the splitter can't push the right columns
    // off-screen. The cap = total body width minus everything to the right of the
    // panel (center min + right panel current + right rail current).
    private void UpdateLeftPanelMaxWidth()
    {
        double available = BodyGrid.ActualWidth;
        if (available <= 0) 
            return;

        double rightPanelW = RightPanelColumn.Width.IsAbsolute ? RightPanelColumn.Width.Value : 0;
        double rightRailW = RightRailColumn.Width.IsAbsolute ? RightRailColumn.Width.Value : 0;
        double reserved = LeftRailWidth + CenterMinWidth + rightPanelW + rightRailW;
        double maxLeft = Math.Max(0, available - reserved);

        LeftPanelColumn.MaxWidth = maxLeft;

        // If the current width exceeds the new cap (e.g. window shrank, right rail
        // appeared), pull it back in immediately — setting MaxWidth alone doesn't
        // shrink an oversized explicit Width.
        if (LeftPanelColumn.Width.IsAbsolute && LeftPanelColumn.Width.Value > maxLeft)
        {
            LeftPanelColumn.Width = new GridLength(maxLeft);
            _lastLeftPanelWidth = maxLeft;
        }
    }

    private void OnBodyGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLeftPanelMaxWidth();
    }

    private void OnLeftSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        // GridSplitter converts the adjacent star column (center) to an explicit
        // pixel width during drag. Restore it so subsequent window resizes can
        // flex the center column again.
        CenterColumn.Width = new GridLength(1, GridUnitType.Star);

        if (LeftPanelColumn.Width.IsAbsolute)
        {
            _lastLeftPanelWidth = LeftPanelColumn.Width.Value;
        }
        UpdateLeftPanelMaxWidth();
    }

}
