using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Flicksy.Agent;

internal sealed class AgentApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly HotKeyWindow _hotKeyWindow;
    private readonly Icon _trayIcon;

    public AgentApplicationContext()
    {
        _hotKeyWindow = new HotKeyWindow(LaunchSnipper);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Snipper", null, (_, _) => LaunchSnipper());
        menu.Items.Add("New Video Project", null, (_, _) => LaunchVideoEditor());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIcon = LoadApplicationIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true,
            Text = "Flicksy Agent",
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => LaunchSnipper();
        _notifyIcon.ShowBalloonTip(
            timeout: 3000,
            tipTitle: "Flicksy Agent",
            tipText: "Listening for Ctrl+Shift+Alt+S to open Flicksy.Snipper.",
            tipIcon: ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (!ReferenceEquals(_trayIcon, SystemIcons.Application))
            _trayIcon.Dispose();
        _hotKeyWindow.Dispose();
        base.ExitThreadCore();
    }

    private static Icon LoadApplicationIcon()
    {
        string? exePath = Environment.ProcessPath;
        if (exePath is null)
            return SystemIcons.Application;

        Icon? extracted = Icon.ExtractAssociatedIcon(exePath);
        return extracted ?? SystemIcons.Application;
    }

    private void LaunchSnipper()
    {
        string? snipperPath = ResolveSnipperExecutablePath();
        if (string.IsNullOrWhiteSpace(snipperPath))
        {
            ShowNotification("Flicksy.Snipper.exe was not found. Build Flicksy.Snipper first.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = snipperPath,
                WorkingDirectory = Path.GetDirectoryName(snipperPath),
                UseShellExecute = true
            });
        }
        catch (Win32Exception ex)
        {
            ShowNotification($"Unable to start Flicksy.Snipper: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            ShowNotification($"Unable to start Flicksy.Snipper: {ex.Message}");
        }
    }

    private static string? ResolveSnipperExecutablePath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDirectory, "Flicksy.Snipper.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Flicksy.Snipper", "bin", "Debug", "net10.0-windows", "Flicksy.Snipper.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Flicksy.Snipper", "bin", "Release", "net10.0-windows", "Flicksy.Snipper.exe"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void LaunchVideoEditor()
    {
        string? videoEditorPath = ResolveVideoEditorExecutablePath();
        if (string.IsNullOrWhiteSpace(videoEditorPath))
        {
            ShowNotification("Flicksy.VideoEditor.exe was not found. Build Flicksy.VideoEditor first.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = videoEditorPath,
                Arguments = "--new-video-project",
                WorkingDirectory = Path.GetDirectoryName(videoEditorPath),
                UseShellExecute = true
            });
        }
        catch (Win32Exception ex)
        {
            ShowNotification($"Unable to start Flicksy.VideoEditor: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            ShowNotification($"Unable to start Flicksy.VideoEditor: {ex.Message}");
        }
    }

    private static string? ResolveVideoEditorExecutablePath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDirectory, "Flicksy.VideoEditor.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Flicksy.VideoEditor", "bin", "Debug", "net10.0-windows", "Flicksy.VideoEditor.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Flicksy.VideoEditor", "bin", "Release", "net10.0-windows", "Flicksy.VideoEditor.exe"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void ShowNotification(string message)
    {
        _notifyIcon.ShowBalloonTip(
            timeout: 3000,
            tipTitle: "Flicksy Agent",
            tipText: message,
            tipIcon: ToolTipIcon.Warning);
    }
}
