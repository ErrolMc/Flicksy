using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// Drives real playback (issue #11): a <see cref="Stopwatch"/> clock on a
/// <see cref="CompositionTarget.Rendering"/> hook advances <see cref="TransportViewModel.Playhead"/>
/// in step with elapsed time, and the preview (which already observes <c>Playhead</c>) repaints
/// — so video composites on the UI thread with no cross-thread bitmap hand-off (ADR 0005, v1).
/// Audio plays in parallel through NAudio: a <see cref="CompositorSampleProvider"/> pulls the
/// <see cref="IAudioMixer"/> on the device's own thread.
/// <para>
/// A/V sync is system-clock master (video timed off the Stopwatch, audio pushed open-loop);
/// they resync on every play / pause / seek / scrub. The engine owns the audio output and the
/// mixer; <see cref="VideoEditorViewModel"/> owns and disposes the engine.
/// </para>
/// </summary>
public sealed class PlaybackEngine : IPlaybackController, IDisposable
{
    private const int AudioLatencyMs = 100;

    private readonly Project.Project _project;
    private readonly TransportViewModel _transport;
    private readonly IAudioMixer _mixer;
    private readonly CompositorSampleProvider _audioProvider;
    private readonly Stopwatch _clock = new();

    private IWavePlayer? _output;
    private bool _renderingHooked;
    // Guards the engine's own Playhead writes so the tick→Playhead→handler path doesn't
    // mistake them for an external seek and re-baseline the clock against itself.
    private bool _suppressPlayheadHandler;
    // Playhead at the moment playback (re)started or last re-synced; the tick computes the
    // current frame as _baseFrame + elapsed.
    private int _baseFrame;
    private bool _disposed;

    public PlaybackEngine(Project.Project project, TransportViewModel transport)
    {
        _project = project;
        _transport = transport;

        _mixer = new AudioMixer();
        var format = WaveFormat.CreateIeeeFloatWaveFormat(project.Settings.AudioSampleRate, 2);
        _audioProvider = new CompositorSampleProvider(_mixer, project, format);
        TryInitAudioOutput();

        _transport.PropertyChanged += OnTransportPropertyChanged;
    }

    public void TogglePlayPause()
    {
        if (_transport.IsPlaying) Pause();
        else Play();
    }

    public void Play()
    {
        if (_disposed) return;

        int total = _transport.TotalFrames;
        if (total <= 0) return;          // nothing to play
        if (_project.Settings.Framerate <= 0) return;

        // Play-from-end restarts at frame 0 (mirrors FFmpegVideoPlayer).
        if (_transport.Playhead >= total - 1)
        {
            SetPlayheadInternal(0);
        }

        _baseFrame = _transport.Playhead;
        _audioProvider.SeekTo(_baseFrame);
        _clock.Restart();
        HookRendering();
        TryStartOutput();
        _transport.IsPlaying = true;
    }

    public void Pause()
    {
        if (_disposed) return;

        _clock.Stop();
        UnhookRendering();
        try { _output?.Pause(); } catch { /* device may be gone */ }
        _transport.IsPlaying = false;
    }

    public void StepFrame(int delta)
    {
        if (_disposed) return;

        Pause();

        int total = _transport.TotalFrames;
        int target = _transport.Playhead + delta;
        if (target < 0) target = 0;
        if (total > 0 && target > total) target = total;

        // External (non-suppressed) write → OnTransportPropertyChanged re-baselines the clock
        // and seeks audio; the preview repaints off the same Playhead change.
        _transport.Playhead = target;
    }

    private void OnTransportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TransportViewModel.Playhead)) return;
        if (_suppressPlayheadHandler) return;

        // External playhead change — scrub, ruler/timeline click, frame step, or a future
        // programmatic seek. Re-base the clock and audio cursor on it so playback (if any)
        // continues seamlessly from the new position and a paused seek lands frame-accurately.
        _baseFrame = _transport.Playhead;
        _audioProvider.SeekTo(_baseFrame);
        if (_transport.IsPlaying) _clock.Restart();
        else _clock.Reset();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_disposed || !_transport.IsPlaying) return;

        int framerate = _project.Settings.Framerate;
        int total = _transport.TotalFrames;
        if (framerate <= 0 || total <= 0)
        {
            Pause();
            return;
        }

        int frame = _baseFrame + (int)(_clock.Elapsed.TotalSeconds * framerate);

        if (frame >= total)
        {
            // Reached the end: hold the last content frame, stop.
            SetPlayheadInternal(total - 1);
            Pause();
            return;
        }

        if (frame != _transport.Playhead)
        {
            SetPlayheadInternal(frame);
        }
    }

    /// <summary>Write the playhead without triggering the external-seek re-base in our own handler.</summary>
    private void SetPlayheadInternal(int frame)
    {
        _suppressPlayheadHandler = true;
        try
        {
            _transport.Playhead = frame;
        }
        finally
        {
            _suppressPlayheadHandler = false;
        }
    }

    // ---- Rendering hook -----------------------------------------------------

    private void HookRendering()
    {
        if (_renderingHooked) return;
        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    private void UnhookRendering()
    {
        if (!_renderingHooked) return;
        CompositionTarget.Rendering -= OnRendering;
        _renderingHooked = false;
    }

    // ---- Audio output -------------------------------------------------------

    private void TryStartOutput()
    {
        try { _output?.Play(); }
        catch (Exception ex) { Debug.WriteLine($"PlaybackEngine: audio output failed to start: {ex.Message}"); }
    }

    /// <summary>
    /// Build the WASAPI shared-mode output. The provider produces stereo float at the project
    /// sample rate; if the device's shared mix rate differs we resample to it (shared mode
    /// requires a format the engine accepts). On any failure the engine plays video only —
    /// audio is best-effort, never a reason to break playback.
    /// </summary>
    private void TryInitAudioOutput()
    {
        try
        {
            int deviceRate = GetDeviceMixRate();
            ISampleProvider chain = _audioProvider;
            if (deviceRate > 0 && deviceRate != _audioProvider.WaveFormat.SampleRate)
            {
                chain = new WdlResamplingSampleProvider(_audioProvider, deviceRate);
            }

            var output = new WasapiOut(AudioClientShareMode.Shared, AudioLatencyMs);
            output.Init(new SampleToWaveProvider(chain));
            _output = output;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PlaybackEngine: audio output unavailable, playing video only: {ex.Message}");
            _output = null;
        }
    }

    private static int GetDeviceMixRate()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return 0;
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.AudioClient.MixFormat.SampleRate;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _transport.PropertyChanged -= OnTransportPropertyChanged;
        UnhookRendering();
        _clock.Stop();

        try { _output?.Stop(); } catch { /* ignore */ }
        try { _output?.Dispose(); } catch { /* ignore */ }
        _output = null;

        _mixer.Dispose();
    }
}
