using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Flicksy.Snipper.Overlays;

namespace Flicksy.Snipper;

internal sealed class SnipperSessionController : IDisposable
{
    private readonly System.Windows.Application _application;
    private readonly ScreenRecorder _screenRecordingService;
    private PreSnipOverlayWindow? _overlayWindow;
    private VideoRecordingOverlayWindow? _videoOverlayWindow;
    private bool _isShuttingDown;

    public SnipperSessionController(System.Windows.Application application)
    {
        _application = application;
        _screenRecordingService = new ScreenRecorder(OnRecordingCompleted);
    }

    public void Start()
    {
        ShowPreSnipOverlay();
    }

    public void Dispose()
    {
        _screenRecordingService.Dispose();
        _overlayWindow?.Close();
        _videoOverlayWindow?.Close();
    }

    private void ShowPreSnipOverlay()
    {
        System.Drawing.Point cursor = Cursor.Position;
        Rectangle bounds = Screen.FromPoint(cursor).Bounds;

        var overlay = new PreSnipOverlayWindow(bounds, OnSnipCaptured, ShowVideoRecordingOverlay);
        _overlayWindow = overlay;
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(_overlayWindow, overlay))
            {
                _overlayWindow = null;
            }

            TryShutdownWhenIdle();
        };
        overlay.Show();
        overlay.Activate();
    }

    private void ShowVideoRecordingOverlay(Rectangle bounds, Rectangle selectionRect)
    {
        var overlay = new VideoRecordingOverlayWindow(
            bounds,
            selectionRect,
            _screenRecordingService.StartRecording,
            _screenRecordingService.StopRecording,
            ShowPreSnipOverlay);
        _videoOverlayWindow = overlay;
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(_videoOverlayWindow, overlay))
            {
                _videoOverlayWindow = null;
            }

            TryShutdownWhenIdle();
        };
        overlay.Show();
        overlay.Activate();
    }

    private void OnSnipCaptured(Bitmap snipBitmap)
    {
        string? snipPath = null;
        try
        {
            snipPath = Path.Combine(Path.GetTempPath(), $"flicksy-snip-{Guid.NewGuid():N}.png");
            snipBitmap.Save(snipPath, ImageFormat.Png);
            using var clipboardImage = new Bitmap(snipBitmap);
            System.Windows.Forms.Clipboard.SetImage(clipboardImage);

            LaunchPostSnipWithMedia(snipPath);
        }
        catch (ExternalException ex)
        {
            System.Windows.MessageBox.Show($"Unable to copy snip to clipboard.\n{ex.Message}", "Flicksy Snipper", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Windows.MessageBox.Show($"Unable to save snip file.\n{ex.Message}", "Flicksy Snipper", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (IOException ex)
        {
            System.Windows.MessageBox.Show($"Unable to save snip file.\n{ex.Message}", "Flicksy Snipper", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            snipBitmap.Dispose();
        }
    }

    private void OnRecordingCompleted(string path)
    {
        LaunchPostSnipWithMedia(path);
    }

    private void LaunchPostSnipWithMedia(string mediaPath)
    {
        if (TryLaunchPostSnipWithMedia(mediaPath, out var errorMessage))
        {
            return;
        }

        System.Windows.MessageBox.Show(
            $"{errorMessage}\n\nSaved media:\n{mediaPath}",
            "Flicksy Snipper",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    internal bool TryLaunchPostSnipWithMedia(string mediaPath, out string? errorMessage)
    {
        string? postSnipPath = ResolvePostSnipExecutablePath();
        if (string.IsNullOrWhiteSpace(postSnipPath))
        {
            errorMessage = "Flicksy.PostSnip.exe was not found. Build Flicksy.PostSnip first.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = postSnipPath,
                Arguments = $"\"{mediaPath}\"",
                WorkingDirectory = Path.GetDirectoryName(postSnipPath),
                UseShellExecute = true
            });

            errorMessage = null;
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            errorMessage = $"Unable to start Flicksy.PostSnip:\n{ex.Message}";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            errorMessage = $"Unable to start Flicksy.PostSnip:\n{ex.Message}";
            return false;
        }
    }

    private static string? ResolvePostSnipExecutablePath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDirectory, "Flicksy.PostSnip.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Flicksy.PostSnip", "bin", "Debug", "net10.0-windows", "Flicksy.PostSnip.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Flicksy.PostSnip", "bin", "Release", "net10.0-windows", "Flicksy.PostSnip.exe"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void TryShutdownWhenIdle()
    {
        if (_isShuttingDown || _overlayWindow is not null || _videoOverlayWindow is not null)
        {
            return;
        }

        _isShuttingDown = true;
        _application.Shutdown();
    }
}
