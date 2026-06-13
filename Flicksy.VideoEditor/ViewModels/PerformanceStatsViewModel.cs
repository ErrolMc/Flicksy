using System;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Flicksy.VideoEditor.Services;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Backs the preview performance HUD (<c>PerformanceStatsView</c>). Owns the displayed stats and
/// their formatting; <see cref="PreviewViewModel"/> is the measurer — it times each compositor
/// paint on the UI thread and reports decode misses here via <see cref="RecordComposite"/> /
/// <see cref="RecordDroppedFrame"/>. FPS, frame-time and dropped counts are averaged over a fixed
/// window and republished at <see cref="PublishIntervalMs"/> (so the bound text doesn't churn at
/// the per-frame rate); the preview resolution line is event-driven (<see cref="SetResolution"/>).
/// <para>
/// Visibility tracks the app-wide <see cref="VideoEditorSettings.ShowPerformanceStats"/> flag — the
/// same observable the Settings overlay toggles — so flipping it shows/hides the HUD live. Every
/// member runs on the UI thread (the render-path invariant <see cref="PreviewViewModel"/> upholds),
/// so no locking is needed.
/// </para>
/// </summary>
public partial class PerformanceStatsViewModel : ObservableObject, IDisposable
{
    private readonly VideoEditorSettings _settings;

    // Republish cadence for the averaged stats — the window over which FPS / mean frame-time /
    // dropped-rate are accumulated before being formatted onto the bound properties.
    private readonly Stopwatch _window = new();
    private int _frames;          // composited frames this window
    private int _dropped;         // playback BeginFrame misses this window (held previous frame)
    private double _compositeMs;  // summed composite time this window
    private const double PublishIntervalMs = 500; // ~2 Hz — avoids per-frame PropertyChanged churn

    /// <summary>Mirrors <see cref="VideoEditorSettings.ShowPerformanceStats"/>; gates the HUD's visibility.</summary>
    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string fpsText = "—";

    [ObservableProperty]
    private string frameTimeText = "—";

    [ObservableProperty]
    private string droppedText = "—";

    [ObservableProperty]
    private string resolutionText = "—";

    public PerformanceStatsViewModel(VideoEditorSettings settings)
    {
        _settings = settings;
        isVisible = settings.ShowPerformanceStats; // backing field — before the subscription
        _settings.PropertyChanged += OnSettingsChanged;
    }

    /// <summary>Record one composited frame and its UI-thread paint cost (ms). Fed from the render path.</summary>
    public void RecordComposite(double milliseconds)
    {
        _frames++;
        _compositeMs += milliseconds;
        MaybePublish();
    }

    /// <summary>Record a playback frame the decoder couldn't supply in time (the pump held the previous frame).</summary>
    public void RecordDroppedFrame()
    {
        _dropped++;
        MaybePublish();
    }

    /// <summary>
    /// Update the preview-resolution line. Event-driven (only when the target bitmap is recreated —
    /// a resolution or quality change), so it stays correct even while paused and never waits on the
    /// averaging window.
    /// </summary>
    public void SetResolution(int width, int height, string qualityLabel)
    {
        ResolutionText = $"{width}×{height} · {qualityLabel}";
    }

    private void MaybePublish()
    {
        if (!IsVisible)
            return;

        if (!_window.IsRunning)
            _window.Restart();

        double elapsedMs = _window.Elapsed.TotalMilliseconds;
        if (elapsedMs < PublishIntervalMs)
            return;

        double seconds = elapsedMs / 1000.0;
        FpsText = (_frames / seconds).ToString("0.0");
        FrameTimeText = _frames > 0
            ? (_compositeMs / _frames).ToString("0.0") + " ms"
            : "—";
        DroppedText = (_dropped / seconds).ToString("0") + "/s";

        ResetWindow();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VideoEditorSettings.ShowPerformanceStats))
            return;

        IsVisible = _settings.ShowPerformanceStats;
        // Re-enabling: drop whatever stale counts accrued so the first publish reflects only
        // post-enable frames.
        if (IsVisible)
            ResetWindow();
    }

    private void ResetWindow()
    {
        _frames = 0;
        _dropped = 0;
        _compositeMs = 0;
        _window.Restart();
    }

    public void Dispose()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
    }
}
