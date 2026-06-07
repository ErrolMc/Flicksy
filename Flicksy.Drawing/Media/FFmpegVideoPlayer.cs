using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;

namespace Flicksy.Drawing.Media;

public sealed class FFmpegVideoPlayer : IVideoPlayer
{
    private const int FrameQueueCapacity = 6;
    private static readonly TimeSpan PositionNotifyInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan FrameDueTolerance = TimeSpan.FromMilliseconds(16);

    private readonly Lock _seekLock = new();
    private readonly Stopwatch _clock = new();

    private MediaFile? _file;
    private BlockingCollection<VideoFrame>? _frames;
    private CancellationTokenSource? _decodeCts;
    private Task? _decodeTask;
    private VideoFrame? _pendingFrame;

    private bool _renderingHooked;
    private bool _disposed;
    private bool _mediaEndedRaised;

    private TimeSpan _clockOffset = TimeSpan.Zero;
    private TimeSpan _position = TimeSpan.Zero;
    private TimeSpan _lastNotifiedPosition = TimeSpan.MinValue;
    private PlaybackState _state = PlaybackState.Idle;

    public PlaybackState State => _state;
    public TimeSpan Position => _position;
    public TimeSpan Duration { get; private set; }
    public TimeSpan FrameDuration { get; private set; }
    public int FrameWidth { get; private set; }
    public int FrameHeight { get; private set; }

    public event EventHandler<VideoFrame>? FrameReady;
    public event EventHandler? PositionChanged;
    public event EventHandler? StateChanged;
    public event EventHandler? MediaEnded;

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        CloseInternal();

        SetState(PlaybackState.Loading);

        await Task.Run(() =>
        {
            var options = new MediaOptions
            {
                StreamsToLoad = MediaMode.Video,
                VideoPixelFormat = ImagePixelFormat.Bgra32,
            };
            MediaFile file = MediaFile.Open(path, options);
            _file = file;
            Duration = file.Video.Info.Duration;
            FrameWidth = file.Video.Info.FrameSize.Width;
            FrameHeight = file.Video.Info.FrameSize.Height;
            double fps = file.Video.Info.AvgFrameRate;
            FrameDuration = fps > 0 ? TimeSpan.FromSeconds(1.0 / fps) : TimeSpan.FromMilliseconds(33);
        }, cancellationToken).ConfigureAwait(true);

        _frames = new BlockingCollection<VideoFrame>(boundedCapacity: FrameQueueCapacity);
        _decodeCts = new CancellationTokenSource();
        _clock.Reset();
        _clockOffset = TimeSpan.Zero;
        _position = TimeSpan.Zero;
        _lastNotifiedPosition = TimeSpan.MinValue;
        _mediaEndedRaised = false;
        _pendingFrame = null;

        BlockingCollection<VideoFrame> framesRef = _frames;
        MediaFile? fileRef = _file;
        CancellationToken ct = _decodeCts.Token;
        _decodeTask = Task.Run(() => DecodeLoop(fileRef!, framesRef, ct), ct);

        var first = await Task.Run(() =>
        {
            if (framesRef.TryTake(out var f, 2000, ct))
            {
                return (VideoFrame?)f;
            }
            return null;
        }, ct).ConfigureAwait(true);

        _pendingFrame = first;

        HookRendering();
        SetState(PlaybackState.Paused);
    }

    public void Play()
    {
        EnsureNotDisposed();
        if (_file is null) 
            return;

        if (_state == PlaybackState.Ended)
        {
            DoSeek(TimeSpan.Zero, resumePlayback: false);
            _mediaEndedRaised = false;
        }

        _clock.Start();
        SetState(PlaybackState.Playing);
    }

    public void Pause()
    {
        EnsureNotDisposed();
        if (_file is null) 
            return;

        if (_state == PlaybackState.Playing)
        {
            _clockOffset += _clock.Elapsed;
            _clock.Reset();
            SetState(PlaybackState.Paused);
        }
    }

    public void Seek(TimeSpan position)
    {
        EnsureNotDisposed();
        if (_file is null) 
            return;

        var clamped = position;
        if (clamped < TimeSpan.Zero) 
            clamped = TimeSpan.Zero;

        if (Duration > TimeSpan.Zero && clamped > Duration) 
            clamped = Duration;

        bool wasPlaying = _state == PlaybackState.Playing;
        DoSeek(clamped, resumePlayback: wasPlaying);
    }

    public void Close() => CloseInternal();

    public void Dispose()
    {
        if (_disposed) 
            return;

        _disposed = true;
        CloseInternal();
    }

    private void DoSeek(TimeSpan target, bool resumePlayback)
    {
        lock (_seekLock)
        {
            if (_file is null) 
                return;

            // Stop decoder
            _decodeCts?.Cancel();
            try
            {
                _decodeTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Decoder may exit via cancellation; ignore.
            }
            _decodeCts?.Dispose();
            _decodeCts = null;
            _decodeTask = null;

            // Drain queue + pending
            if (_frames is not null)
            {
                while (_frames.TryTake(out var leftover))
                {
                    ArrayPool<byte>.Shared.Return(leftover.Buffer);
                }
                _frames.Dispose();
                _frames = null;
            }

            if (_pendingFrame.HasValue)
            {
                ArrayPool<byte>.Shared.Return(_pendingFrame.Value.Buffer);
                _pendingFrame = null;
            }

            // Reset clock to target
            _clock.Stop();
            _clock.Reset();
            _clockOffset = target;
            _position = target;
            _lastNotifiedPosition = TimeSpan.MinValue;
            _mediaEndedRaised = false;

            // Seek + decode the target frame inline so the user sees an immediate update.
            try
            {
                ImageData image = _file.Video.GetFrame(target);
                Span<byte> data = image.Data;
                int len = data.Length;
                byte[] buf = ArrayPool<byte>.Shared.Rent(len);
                data.CopyTo(buf);

                _pendingFrame = new VideoFrame(
                    buf, len,
                    image.ImageSize.Width, image.ImageSize.Height,
                    image.Stride, target);
            }
            catch
            {
                // If seek fails we still continue; the next decode loop iteration will recover.
            }

            // Restart decoder from the new position
            _frames = new BlockingCollection<VideoFrame>(boundedCapacity: FrameQueueCapacity);
            _decodeCts = new CancellationTokenSource();
            BlockingCollection<VideoFrame> framesRef = _frames;
            MediaFile fileRef = _file;
            CancellationToken ct = _decodeCts.Token;
            _decodeTask = Task.Run(() => DecodeLoop(fileRef, framesRef, ct), ct);

            if (resumePlayback)
            {
                _clock.Start();
                SetState(PlaybackState.Playing);
            }
            else if (_state == PlaybackState.Ended)
            {
                SetState(PlaybackState.Paused);
            }

            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void DecodeLoop(MediaFile file, BlockingCollection<VideoFrame> frames, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!file.Video.TryGetNextFrame(out var image))
                {
                    frames.CompleteAdding();
                    return;
                }

                Span<byte> data = image.Data;
                int len = data.Length;
                byte[] buf = ArrayPool<byte>.Shared.Rent(len);
                data.CopyTo(buf);

                TimeSpan pts = file.Video.Position;
                var frame = new VideoFrame(
                    buf, len,
                    image.ImageSize.Width, image.ImageSize.Height,
                    image.Stride,
                    pts);

                try
                {
                    frames.Add(frame, ct);
                }
                catch (OperationCanceledException)
                {
                    ArrayPool<byte>.Shared.Return(buf);
                    return;
                }
                catch (InvalidOperationException)
                {
                    // Collection completed externally; return buffer and exit.
                    ArrayPool<byte>.Shared.Return(buf);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancel.
        }
        catch
        {
            try 
            { 
                frames.CompleteAdding(); 
            } 
            catch 
            { 
                // ignore
            }
        }
    }

    private void HookRendering()
    {
        if (_renderingHooked) 
            return;

        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    private void UnhookRendering()
    {
        if (!_renderingHooked) 
            return;

        CompositionTarget.Rendering -= OnRendering;
        _renderingHooked = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        VideoFrame? toPresent = null;
        bool raisePositionChanged = false;

        // TryEnter (not lock) because scrub seeks run on a background thread and
        // hold _seekLock for tens of ms each. Blocking here would stall the UI
        // thread for the full seek duration. Skipping a tick is harmless — the
        // new frame will be presented on the next composition pass.
        if (!_seekLock.TryEnter())
        {
            return;
        }
        try
        {
            if (_frames is null) 
                return;

            TimeSpan elapsed = _clockOffset + _clock.Elapsed;

            if (!_pendingFrame.HasValue && _frames.TryTake(out var firstAvailable))
            {
                _pendingFrame = firstAvailable;
            }

            while (_pendingFrame.HasValue && _pendingFrame.Value.Pts <= elapsed + FrameDueTolerance)
            {
                if (toPresent.HasValue)
                {
                    ArrayPool<byte>.Shared.Return(toPresent.Value.Buffer);
                }
                toPresent = _pendingFrame;
                _pendingFrame = _frames.TryTake(out var next) ? next : null;
            }

            if (toPresent.HasValue)
            {
                _position = toPresent.Value.Pts;
                if (_lastNotifiedPosition == TimeSpan.MinValue
                    || (_position - _lastNotifiedPosition).Duration() >= PositionNotifyInterval)
                {
                    _lastNotifiedPosition = _position;
                    raisePositionChanged = true;
                }
            }
        }
        finally
        {
            _seekLock.Exit();
        }

        if (toPresent.HasValue)
        {
            VideoFrame f = toPresent.Value;
            try
            {
                FrameReady?.Invoke(this, f);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(f.Buffer);
            }
        }

        if (raisePositionChanged)
        {
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        DetectEndOfStream();
    }

    private void DetectEndOfStream()
    {
        bool transitioned = false;

        // Same TryEnter rationale as OnRendering: never block the UI thread on a
        // background seek. End-of-stream can be detected on the next tick.
        if (!_seekLock.TryEnter())
        {
            return;
        }
        try
        {
            if (_mediaEndedRaised 
                || _frames is null 
                || !_frames.IsAddingCompleted 
                || (_pendingFrame.HasValue || _frames.Count > 0) 
                || _state == PlaybackState.Ended)
            {
                return;
            }

            _mediaEndedRaised = true;
            _clock.Stop();
            _clock.Reset();
            _clockOffset = Duration;
            _position = Duration;
            _lastNotifiedPosition = Duration;
            transitioned = true;
        }
        finally
        {
            _seekLock.Exit();
        }

        if (transitioned)
        {
            SetState(PlaybackState.Ended);
            PositionChanged?.Invoke(this, EventArgs.Empty);
            MediaEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetState(PlaybackState newState)
    {
        if (_state == newState) 
            return;

        _state = newState;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseInternal()
    {
        UnhookRendering();

        _decodeCts?.Cancel();
        try
        {
            _decodeTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignored; decoder may have exited via cancellation.
        }
        _decodeCts?.Dispose();
        _decodeCts = null;
        _decodeTask = null;

        if (_pendingFrame.HasValue)
        {
            ArrayPool<byte>.Shared.Return(_pendingFrame.Value.Buffer);
            _pendingFrame = null;
        }

        if (_frames is not null)
        {
            while (_frames.TryTake(out var f))
            {
                ArrayPool<byte>.Shared.Return(f.Buffer);
            }
            try 
            { 
                _frames.Dispose(); 
            } 
            catch 
            {
                // ignore
            }
            _frames = null;
        }

        if (_file is not null)
        {
            try 
            { 
                _file.Dispose(); 
            } 
            catch 
            { 
                // ignore
            }
            _file = null;
        }

        _clock.Stop();
        _clock.Reset();
        _clockOffset = TimeSpan.Zero;
        _position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        FrameWidth = 0;
        FrameHeight = 0;
        _lastNotifiedPosition = TimeSpan.MinValue;
        _mediaEndedRaised = false;

        SetState(PlaybackState.Idle);
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FFmpegVideoPlayer));
        }
    }
}
