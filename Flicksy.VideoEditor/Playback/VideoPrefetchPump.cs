using System;
using System.Collections.Generic;
using System.Threading;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// Off-thread video decode-ahead worker (ADR 0009 — the "phase 2" ADR 0005 deferred). A producer
/// thread decodes upcoming frames into a bounded queue so the UI-thread render tick only composites
/// already-decoded frames and never blocks on the codec. Moves <b>decode</b> off the UI thread;
/// compositing (and <c>GraphicsClip</c>'s <c>RenderTargetBitmap</c>, and the unfrozen
/// <c>WriteableBitmap</c>) stays on the UI thread. The producer decodes strictly forward (cheap
/// sequential reads); the consumer presents the newest ready frame at or before the playhead. When
/// the decoder can't sustain realtime it falls behind, and once it trails the playhead by more than
/// the resync threshold it jumps forward to the playhead in one seek (<em>bounded resync</em>) so
/// video never drifts unboundedly behind the open-loop audio — playback drops intermediate frames
/// but stays live and A/V-synced within the threshold, never a UI stall.
/// <para>
/// <b>Threading.</b> One <c>lock (_gate)</c> guards <c>{_queue, _generation, _nextFrame, _scale,
/// _running}</c> and is never held across a decode (decode runs in <see cref="ProducerLoop"/>
/// outside the lock). <c>_current</c> is touched only on the UI thread
/// (<see cref="BeginFrame"/>/<see cref="EndFrame"/>/<see cref="Acquire"/>/<see cref="SeekTo"/>,
/// strictly paired, single-frame-in-flight) so it needs no lock. Buffer ownership is a strict
/// baton: rented by the producer → owned by the queue at enqueue → owned by the consumer between a
/// true <see cref="BeginFrame"/> and <see cref="EndFrame"/> → returned. A <b>generation</b> stamp
/// makes seek authoritative: <see cref="SeekTo"/> bumps it and drains under the lock; the producer
/// only enqueues if the generation still matches, so no stale-epoch (e.g. pre-seek) bundle ever
/// reaches the consumer.
/// </para>
/// </summary>
public sealed class VideoPrefetchPump : IPlaybackFrameSource, IDisposable
{
    private const int DefaultDepth = 12;

    // Backstop so the producer re-checks TotalFrames (a clip added mid-play) and self-heals any
    // missed pulse without ever deadlocking. Slot-free/seek/stop pulse it immediately; this only
    // matters while it would otherwise idle at the end of the timeline.
    private const int ProducerIdleWaitMs = 100;

    // How far the producer may trail the playhead before resyncing (one forward seek to it). Must be
    // large enough that the jump exceeds FFMediaToolkit's ~500ms VideoSeekThreshold so it actually
    // skips decode work, but small enough that video doesn't drift noticeably behind audio. The
    // engine passes the timeline framerate (~1s); this is the standalone fallback.
    private const int DefaultResyncThresholdFrames = 30;

    // Monitor condition variable (Wait/Pulse below), not just a mutex — must stay `object`.
    // System.Threading.Lock is mutual-exclusion only; it has no Wait/Pulse equivalent.
    private readonly object _gate = new();
    private readonly Queue<FrameBundle> _queue = new();
    private readonly IFrameBundleSource _source;
    private readonly int _depth;
    private readonly int _resyncThresholdFrames;

    private int _generation;
    private int _nextFrame;
    // The frame the consumer most recently asked for. The producer decodes forward, but if it trails
    // this by more than _resyncThresholdFrames (decode slower than realtime) it jumps here in one
    // seek — dropping the backlog to stay synced rather than drifting ever further behind the audio.
    private int _lastConsumerFrame;
    private double _scale = 1.0;
    private bool _running;
    private Thread? _thread;
    private bool _disposed;

    // UI-thread-only: the bundle claimed by the in-flight BeginFrame/EndFrame pair.
    private FrameBundle? _current;

    public VideoPrefetchPump(IFrameBundleSource source, int resyncThresholdFrames = DefaultResyncThresholdFrames, int depth = DefaultDepth)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _depth = depth > 0 ? depth : DefaultDepth;
        _resyncThresholdFrames = resyncThresholdFrames > 0 ? resyncThresholdFrames : DefaultResyncThresholdFrames;
    }

    // ---- Producer control (UI thread) --------------------------------------

    /// <summary>
    /// Begin prefetching from <paramref name="fromFrame"/> at <paramref name="decodeScale"/>. No-op
    /// if already running (use <see cref="Reprime"/>/<see cref="Prefetch"/> to reposition).
    /// </summary>
    public void Start(int fromFrame, double decodeScale = 1.0)
    {
        lock (_gate)
        {
            if (_running) return;
            _generation++;
            _nextFrame = Math.Max(0, fromFrame);
            _lastConsumerFrame = _nextFrame;
            _scale = decodeScale;
            _running = true;
            _thread = new Thread(ProducerLoop) { IsBackground = true, Name = "VideoPrefetchPump" };
            _thread.Start();
        }
    }

    /// <summary>Whether the producer thread is currently prefetching. UI thread.</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    /// <summary>
    /// Stop prefetching: join the producer thread, then drain the queue (returning every buffer).
    /// Keeps the underlying decoder cache alive for a fast restart; only <see cref="Dispose"/> tears
    /// it down. Safe to call when not running.
    /// </summary>
    public void Stop()
    {
        Thread? t;
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            t = _thread;
            _thread = null;
            Monitor.PulseAll(_gate);
        }

        t?.Join(); // producer is gone after this — no decode can be in flight

        DrainAll();
    }

    /// <summary>
    /// Reposition prefetch to <paramref name="frame"/>, preserving the current decode scale. Any
    /// bundle the producer is mid-decoding self-cancels (generation mismatch at enqueue). UI thread.
    /// </summary>
    public void SeekTo(int frame)
    {
        double scale;
        lock (_gate) scale = _scale;
        Reprime(frame, scale);
    }

    /// <summary>
    /// Reposition prefetch to <paramref name="frame"/> at <paramref name="decodeScale"/>: bump the
    /// generation, drain the queue, and re-prime from there. Like <see cref="SeekTo"/> but also sets
    /// the decode scale — used to warm the buffer while paused at the preview's scale. UI thread.
    /// </summary>
    public void Reprime(int frame, double decodeScale)
    {
        List<FrameBundle> dropped;
        lock (_gate)
        {
            _generation++;
            _nextFrame = Math.Max(0, frame);
            _lastConsumerFrame = _nextFrame;
            _scale = decodeScale;
            dropped = DrainQueueLocked();
            Monitor.PulseAll(_gate);
        }

        foreach (var b in dropped) _source.Recycle(b);

        // _current is UI-thread-only; recycle + clear it too (usually already null between ticks).
        var cur = _current;
        _current = null;
        if (cur is not null) _source.Recycle(cur);
    }

    /// <summary>
    /// Begin or reposition background prefetch from <paramref name="fromFrame"/> at
    /// <paramref name="decodeScale"/> with no consumer attached — used while paused so the decoder
    /// and buffer are warm before play, removing the cold-start hitch. Starts the producer if idle,
    /// otherwise re-primes it at the new position/scale. UI thread.
    /// </summary>
    public void Prefetch(int fromFrame, double decodeScale)
    {
        if (IsRunning) Reprime(fromFrame, decodeScale);
        else Start(fromFrame, decodeScale);
    }

    // ---- Consumer (UI thread) ----------------------------------------------

    public bool BeginFrame(int frame, double decodeScale)
    {
        // Defensive: a well-behaved caller EndFrame()s after every true BeginFrame, so _current is
        // null here. If it isn't (a caller skipped EndFrame, e.g. an exception path that forgot the
        // finally), reclaim it rather than leak. _current is UI-thread-only, so no lock needed.
        if (_current is not null)
        {
            _source.Recycle(_current);
            _current = null;
        }

        List<FrameBundle>? dropped = null;
        bool hit;

        lock (_gate)
        {
            _lastConsumerFrame = frame; // the producer resyncs to this when it trails by > threshold

            if (decodeScale != _scale)
            {
                // Quality changed: every buffered frame is the wrong size. Flush + re-prime at the
                // new scale from this frame; report a miss so the caller holds the previous frame.
                _scale = decodeScale;
                _generation++;
                _nextFrame = frame;
                dropped = DrainQueueLocked();
                Monitor.PulseAll(_gate);
                hit = false;
            }
            else
            {
                // Best-effort live present: take the NEWEST buffered frame at or before the playhead,
                // recycling the older ones it supersedes; leave any future frames queued. A miss
                // (hold previous) only when every buffered frame is still in the future or the queue
                // is empty. This (plus the producer's bounded resync) degrades a slow decoder to
                // dropped frames rather than a freeze — never matching the exact playhead would
                // otherwise discard every frame the producer makes while it trails realtime.
                FrameBundle? chosen = null;
                while (_queue.Count > 0 && _queue.Peek().Frame <= frame)
                {
                    if (chosen is not null) (dropped ??= new List<FrameBundle>()).Add(chosen);
                    chosen = _queue.Dequeue();
                }

                hit = chosen is not null;
                if (hit) _current = chosen;

                Monitor.Pulse(_gate); // freed slot(s) → let the producer run
            }
        }

        if (dropped is not null)
        {
            foreach (var b in dropped) _source.Recycle(b);
        }

        return hit;
    }

    public void EndFrame()
    {
        var done = _current; // UI-thread-only
        _current = null;
        if (done is not null) _source.Recycle(done);
    }

    /// <summary>
    /// True if a decoded frame at or before <paramref name="frame"/> is queued — i.e. the next
    /// <see cref="BeginFrame"/> at the current scale would hit. Lets the engine prebuffer on play:
    /// hold the clock at the start frame until the (cold) first decode lands, so playback begins
    /// aligned instead of with the playhead racing ahead of a not-yet-ready decoder. UI thread.
    /// </summary>
    public bool HasReadyFrameAt(int frame)
    {
        lock (_gate)
        {
            return _queue.Count > 0 && _queue.Peek().Frame <= frame;
        }
    }

    public IReadOnlyList<CompositionLayer> CurrentLayers =>
        _current?.Layers ?? Array.Empty<CompositionLayer>();

    public VideoFrame? Acquire(MediaClip clip, TimeSpan sourceTime, double decodeScale)
    {
        // Serve the pre-decoded frame for the claimed bundle. sourceTime/decodeScale are ignored —
        // the bundle is already the right frame at the right scale (chosen in BeginFrame).
        var current = _current;
        if (current is not null && current.Frames.TryGetValue(clip.Id, out var frame))
        {
            return frame;
        }
        return null;
    }

    public void Release(VideoFrame frame)
    {
        // No-op: the whole bundle is recycled at EndFrame. (The compositor calls this per layer.)
    }

    // ---- Producer thread ----------------------------------------------------

    private void ProducerLoop()
    {
        while (true)
        {
            int gen, frame;
            double scale;

            lock (_gate)
            {
                // Decode forward with cheap sequential reads — no per-frame seeks. The consumer drops
                // frames it has passed (best-effort newest ≤ playhead), so we never decode-and-discard.
                // But a sub-realtime decoder would otherwise trail the playhead unboundedly, drifting
                // video ever further behind the (open-loop) audio. So once we've fallen more than
                // _resyncThresholdFrames behind, jump _nextFrame up to the playhead: one forward seek
                // that drops the backlog and re-syncs, then resume sequential decode. The jump is large
                // (> threshold) so it clears FFMediaToolkit's ~500ms VideoSeekThreshold and actually
                // skips work, and it fires at most ~once per threshold-window — not the per-frame seek
                // storm that collapses throughput on long-GOP content.
                while (_running && (_queue.Count >= _depth || _nextFrame >= _source.TotalFrames))
                {
                    Monitor.Wait(_gate, ProducerIdleWaitMs);
                }
                if (!_running) return;

                gen = _generation;
                frame = _nextFrame;
                if (_lastConsumerFrame - frame > _resyncThresholdFrames)
                {
                    frame = _lastConsumerFrame; // resync: drop the backlog, decode from the playhead
                }
                scale = _scale;
                _nextFrame = frame + 1;
            }

            // Decode outside the lock (slow). Null = skip this frame (torn read).
            FrameBundle? bundle = _source.Produce(frame, gen, scale);

            bool enqueued = false;
            if (bundle is not null)
            {
                lock (_gate)
                {
                    if (_running && _generation == gen)
                    {
                        _queue.Enqueue(bundle);
                        enqueued = true;
                    }
                }

                // Stale epoch (a seek raced past) or stopped → the bundle's buffers are ours to return.
                if (!enqueued) _source.Recycle(bundle);
            }
        }
    }

    // ---- Helpers ------------------------------------------------------------

    // Drain the queue into a list under the caller's lock; recycle outside the lock.
    private List<FrameBundle> DrainQueueLocked()
    {
        var list = new List<FrameBundle>(_queue.Count);
        while (_queue.Count > 0) list.Add(_queue.Dequeue());
        return list;
    }

    private void DrainAll()
    {
        List<FrameBundle> dropped;
        lock (_gate)
        {
            dropped = DrainQueueLocked();
        }
        foreach (var b in dropped) _source.Recycle(b);

        var cur = _current;
        _current = null;
        if (cur is not null) _source.Recycle(cur);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();              // joins the producer thread + drains the queue
        _source.Dispose();   // only now is it safe to dispose the decoder cache (no decode in flight)
    }
}
