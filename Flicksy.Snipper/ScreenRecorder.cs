using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;

namespace Flicksy.Snipper;

internal sealed class ScreenRecorder(Action<string> onRecordingCompleted) : IDisposable
{
    private Process? _ffmpegProcess;
    private string? _recordingPath;
    private readonly StringBuilder _ffmpegErrorBuffer = new();

    public void StartRecording(Rectangle screenBounds, Rectangle selectionRect)
    {
        if (_ffmpegProcess is not null)
        {
            return;
        }

        _recordingPath = Path.Combine(Path.GetTempPath(), $"flicksy-recording-{Guid.NewGuid():N}.mp4");
        string ffmpegArguments = BuildArguments(screenBounds, selectionRect, _recordingPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = ffmpegArguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        _ffmpegProcess = new Process { StartInfo = startInfo };
        _ffmpegProcess.ErrorDataReceived += OnFfmpegErrorDataReceived;
        _ffmpegProcess.Start();
        _ffmpegProcess.BeginErrorReadLine();
        _ffmpegProcess.BeginOutputReadLine();
    }

    public void StopRecording()
    {
        if (_ffmpegProcess is null || string.IsNullOrWhiteSpace(_recordingPath))
        {
            return;
        }

        try
        {
            if (!_ffmpegProcess.HasExited)
            {
                _ffmpegProcess.StandardInput.WriteLine("q");
                _ffmpegProcess.StandardInput.Flush();
            }

            if (!_ffmpegProcess.WaitForExit(5000))
            {
                _ffmpegProcess.Kill(entireProcessTree: true);
                _ffmpegProcess.WaitForExit(2000);
            }

            if (WaitForRecordingFile(_recordingPath))
            {
                onRecordingCompleted(_recordingPath);
            }
        }
        finally
        {
            CleanupProcess();
        }
    }

    public void Dispose()
    {
        CleanupProcess();
    }

    private void OnFfmpegErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            _ffmpegErrorBuffer.AppendLine(e.Data);
        }
    }

    private static string BuildArguments(Rectangle screenBounds, Rectangle selectionRect, string outputPath)
    {
        Rectangle virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        var absoluteCaptureArea = new Rectangle(
            screenBounds.Left + selectionRect.Left,
            screenBounds.Top + selectionRect.Top,
            selectionRect.Width,
            selectionRect.Height);

        Rectangle boundedCaptureArea = Rectangle.Intersect(absoluteCaptureArea, virtualScreen);
        if (boundedCaptureArea.Width <= 1 || boundedCaptureArea.Height <= 1)
        {
            throw new InvalidOperationException("Selected recording area is outside available displays.");
        }

        int captureX = boundedCaptureArea.Left;
        int captureY = boundedCaptureArea.Top;
        int captureWidth = boundedCaptureArea.Width & ~1;
        int captureHeight = boundedCaptureArea.Height & ~1;

        if (captureWidth < 2 || captureHeight < 2)
        {
            throw new InvalidOperationException("Selected recording area is too small.");
        }

        return $"-y -f gdigrab -framerate 30 -offset_x {captureX} -offset_y {captureY} -video_size {captureWidth}x{captureHeight} -i desktop -an -c:v libx264 -preset veryfast -pix_fmt yuv420p \"{outputPath}\"";
    }

    private static bool WaitForRecordingFile(string recordingPath)
    {
        for (int i = 0; i < 20; i++)
        {
            if (File.Exists(recordingPath))
            {
                var fileInfo = new FileInfo(recordingPath);
                if (fileInfo.Length > 0)
                {
                    return true;
                }
            }

            Thread.Sleep(200);
        }

        return false;
    }

    private void CleanupProcess()
    {
        if (_ffmpegProcess is null)
        {
            return;
        }

        _ffmpegProcess.ErrorDataReceived -= OnFfmpegErrorDataReceived;

        if (!_ffmpegProcess.HasExited)
        {
            _ffmpegProcess.Kill(entireProcessTree: true);
            _ffmpegProcess.WaitForExit(2000);
        }

        _ffmpegProcess.Dispose();
        _ffmpegProcess = null;
        _recordingPath = null;
        _ffmpegErrorBuffer.Clear();
    }
}
