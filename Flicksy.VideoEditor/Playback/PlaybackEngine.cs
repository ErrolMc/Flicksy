using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
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
/// — so video composites on the UI thread with no cross-thread bitmap hand-off (ADR 0005). The
/// per-frame video <em>decode</em>, however, now runs ahead on a background thread via a
/// <see cref="VideoPrefetchPump"/> the engine owns (ADR 0009): started on play, stopped on pause,
/// seeked alongside audio, and pointed at the preview through <see cref="IPlaybackFrameSink"/>, so
/// the UI tick only composites frames that are already decoded. Audio plays in parallel through
/// NAudio: a <see cref="CompositorSampleProvider"/> pulls the <see cref="IAudioMixer"/> on the
/// device's own thread.
/// <para>
/// A/V sync is system-clock master (video timed off the Stopwatch, audio pushed open-loop);
/// they resync on every play / pause / seek / scrub. The engine owns the audio output, the mixer,
/// and the video pump; <see cref="VideoEditorViewModel"/> owns and disposes the engine.
/// </para>
/// </summary>
public sealed class PlaybackEngine : IPlaybackController, IDisposable
{
    private const int AudioLatencyMs = 100;

    // Startup prebuffer cap: on play, hold the playhead + audio until the pump has the first frame
    // decoded (the cold first decode includes opening the file), so playback begins aligned instead
    // of the playhead racing a not-yet-ready decoder. If no frame is ready within this budget we
    // start anyway — an empty/stuck decoder must never wedge play.
    private const int MaxPrimeWaitMs = 1000;

    // Debounce before warming the pump after the playhead settles while paused, so rapid scrubbing
    // doesn't churn the decoder; the prefetch kicks in once the user parks (i.e. about to play).
    private const int PrefetchDebounceMs = 200;

    private readonly Project.Project _project;
    private readonly TransportViewModel _transport;
    private readonly IAudioMixer _mixer;
    private readonly CompositorSampleProvider _audioProvider;
    private readonly IPlaybackFrameSink _frameSink;
    private readonly VideoPrefetchPump _videoPump;
    private readonly Stopwatch _clock = new();

    private IWavePlayer? _output;
    private bool _renderingHooked;
    // Guards the engine's own Playhead writes so the tick→Playhead→handler path doesn't
    // mistake them for an external seek and re-baseline the clock against itself.
    private bool _suppressPlayheadHandler;
    // Playhead at the moment playback (re)started or last re-synced; the tick computes the
    // current frame as _baseFrame + elapsed.
    private int _baseFrame;
    // True between play and the first decoded frame being ready: the clock is held at _baseFrame and
    // audio is not yet started, so video, playhead, and audio all begin together (no startup desync).
    private bool _priming;
    // Debounced paused-state prefetch (warm the pump before play). _prefetch{Frame,Scale} record what
    // it's primed at so Play can reuse a warm buffer instead of draining and re-decoding it.
    private readonly DispatcherTimer _prefetchTimer;
    private int _prefetchFrame = -1;
    private double _prefetchScale = double.NaN;
    private bool _disposed;

    public PlaybackEngine(Project.Project project, TransportViewModel transport, IPlaybackFrameSink frameSink)
    {
        _project = project;
        _transport = transport;
        _frameSink = frameSink;

        _mixer = new AudioMixer();
        var format = WaveFormat.CreateIeeeFloatWaveFormat(project.Settings.AudioSampleRate, 2);
        _audioProvider = new CompositorSampleProvider(_mixer, project, format);
        TryInitAudioOutput();

        // Off-thread video decode-ahead (ADR 0009): its own decoder cache, total frames pulled from
        // the transport. Started/stopped with playback; the preview renders from it via _frameSink.
        // Resync threshold = one second of timeline frames: a sub-realtime decoder may trail the
        // playhead by up to ~1s before it jumps forward to re-sync, bounding A/V drift (the jump
        // also clears FFMediaToolkit's ~500ms seek threshold so it actually skips work).
        _videoPump = new VideoPrefetchPump(
            new ProjectBundleSource(project, () => _transport.TotalFrames),
            resyncThresholdFrames: Math.Max(1, project.Settings.Framerate));

        _transport.PropertyChanged += OnTransportPropertyChanged;

        // Debounced background prefetch: when the playhead settles while paused (incl. now, on open),
        // warm the pump at that position so the next Play starts instantly with no decode hitch.
        _prefetchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PrefetchDebounceMs) };
        _prefetchTimer.Tick += OnPrefetchTick;
        SchedulePrefetch();
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

        _prefetchTimer.Stop(); // we position the pump now; cancel any pending debounce

        _baseFrame = _transport.Playhead;
        _audioProvider.SeekTo(_baseFrame);

        // Reuse the paused-state prefetch if it already warmed this exact position + scale (instant
        // start, no re-decode); otherwise (re)prime the pump here.
        double scale = _frameSink.CurrentDecodeScale;
        if (!(_videoPump.IsRunning && _prefetchFrame == _baseFrame && _prefetchScale == scale))
        {
            _videoPump.Prefetch(_baseFrame, scale);
            _prefetchFrame = _baseFrame;
            _prefetchScale = scale;
        }
        _frameSink.PlaybackFrames = _videoPump;

        // Prebuffer: hold here until the pump has the first frame ready (see _priming). The clock
        // doubles as the prime-timeout timer until then; audio output starts when priming completes
        // so A/V begin together — OnRendering finishes the start.
        _priming = true;
        _clock.Restart();
        HookRendering();
        _transport.IsPlaying = true;
    }

    public void Pause()
    {
        if (_disposed) return;

        _clock.Stop();
        _priming = false;
        _prefetchTimer.Stop();
        UnhookRendering();
        _frameSink.PlaybackFrames = null; // preview reverts to synchronous decode (scrub)
        _videoPump.Stop();
        _prefetchFrame = -1; // buffer drained by Stop
        try { _output?.Pause(); } catch { /* device may be gone */ }
        _transport.IsPlaying = false;

        // Re-warm the prefetch at the pause position (debounced) so a subsequent Play reuses a full
        // buffer instead of restarting with an empty queue — the same path a seek-while-paused takes,
        // giving pause→play and seek→play identical warm-up behavior.
        SchedulePrefetch();
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

        // External playhead change — scrub, ruler/timeline click, or a programmatic seek. A manual
        // seek is a take-over, so stop playback if it was running; the user resumes with Play. (The
        // engine's own per-frame tick writes are suppressed above, so this only fires for real user
        // seeks. Frame step pauses on its own before writing, so it lands here already stopped.)
        if (_transport.IsPlaying)
        {
            Pause();
        }

        // Re-base on the new position so the (now paused) seek lands frame-accurately and the next
        // Play starts here; warm the prefetch for it once the playhead settles.
        _baseFrame = _transport.Playhead;
        _audioProvider.SeekTo(_baseFrame);
        _clock.Reset();
        SchedulePrefetch();
    }

    // ---- Background prefetch (warm the pump while paused) -------------------

    /// <summary>(Re)start the debounce window; on expiry <see cref="OnPrefetchTick"/> warms the pump.</summary>
    private void SchedulePrefetch()
    {
        if (_disposed) return;
        _prefetchTimer.Stop();
        _prefetchTimer.Start();
    }

    private void OnPrefetchTick(object? sender, EventArgs e)
    {
        _prefetchTimer.Stop();
        if (_disposed || _transport.IsPlaying) return;
        if (_transport.TotalFrames <= 0 || _project.Settings.Framerate <= 0) return;

        // Playhead has settled while paused: warm the pump (decoder + buffer) here at the preview's
        // scale so the next Play starts instantly. No-op if it's already primed at this spot.
        int frame = _transport.Playhead;
        double scale = _frameSink.CurrentDecodeScale;
        if (_videoPump.IsRunning && _prefetchFrame == frame && _prefetchScale == scale) return;

        _videoPump.Prefetch(frame, scale);
        _prefetchFrame = frame;
        _prefetchScale = scale;
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

        if (_priming)
        {
            // Still prebuffering: keep the playhead at _baseFrame until the first frame is decoded
            // (or the cap elapses). Then realign the clock and start audio so video, playhead, and
            // audio all begin from this instant together — no startup desync, no playhead racing
            // ahead of a cold decoder.
            if (!_videoPump.HasReadyFrameAt(_baseFrame)
                && _clock.Elapsed.TotalMilliseconds < MaxPrimeWaitMs)
            {
                return;
            }
            _priming = false;
            _clock.Restart();   // playback elapsed starts now (≈0 → frame stays _baseFrame)
            TryStartOutput();   // audio begins aligned with the first shown frame
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
        _prefetchTimer.Stop();
        _prefetchTimer.Tick -= OnPrefetchTick;
        UnhookRendering();
        _clock.Stop();

        _frameSink.PlaybackFrames = null;
        _videoPump.Dispose(); // joins the producer thread before its decoder cache is torn down

        try { _output?.Stop(); } catch { /* ignore */ }
        try { _output?.Dispose(); } catch { /* ignore */ }
        _output = null;

        _mixer.Dispose();
    }
}
