using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Flicksy.VideoEditor.Controls;

/// <summary>
/// The custom-chrome title bar strip: app icon, window title, the File/Edit menu bar
/// (commands on <see cref="ViewModels.TitleBarViewModel"/>, the expected DataContext)
/// and the Min/Max/Close caption buttons. Window-level concerns stay in this
/// code-behind: caption clicks drive <see cref="SystemCommands"/> against the host
/// window, and the icon is re-read from the exe because WindowChrome removed the
/// native title bar that would have shown it automatically.
/// </summary>
public partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
        LoadTitleBarIcon();
    }

    // Same pattern as AgentApplicationContext.LoadApplicationIcon: read the
    // exe-embedded <ApplicationIcon> back out. No icon is fine (blank Image).
    private void LoadTitleBarIcon()
    {
        string? exePath = Environment.ProcessPath;
        if (exePath is null)
            return;

        using System.Drawing.Icon? extracted = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        if (extracted is null)
            return;

        TitleBarIcon.Source = Imaging.CreateBitmapSourceFromHIcon(
            extracted.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is Window window)
            SystemCommands.MinimizeWindow(window);
    }

    private void OnMaximizeRestoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not Window window)
            return;

        if (window.WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(window);
        else
            SystemCommands.MaximizeWindow(window);
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is Window window)
            SystemCommands.CloseWindow(window);
    }
}
