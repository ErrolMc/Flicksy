using System;
using System.Collections.Generic;
using System.Threading;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.Playback;

namespace Flicksy.VideoEditor.Tests.Playback;

/// <summary>
/// Deterministic in-memory <see cref="IFrameBundleSource"/> for testing <see cref="VideoPrefetchPump"/>
/// with no FFmpeg / Skia / WPF. Each produced frame carries one fake <see cref="VideoFrame"/> (a
/// freshly-allocated buffer so references are unique — no ArrayPool aliasing) whose <c>Pts</c> encodes
/// the frame number for identity checks. Tracks every outstanding buffer to assert no leak / no
/// double-return; a hold gate lets a test freeze a decode in flight.
/// <para>
/// Determinism without permits: the producer makes frames <c>[0, TotalFrames)</c> then idles in the
/// pump's <c>Monitor.Wait</c> (wakeable by Stop), so set <see cref="TotalFrames"/> to bound the
/// produced range and pick a queue depth to bound how many sit buffered.
/// </para>
/// </summary>
internal sealed class FakeBundleSource : IFrameBundleSource
{
    public static readonly Guid ClipId = new("11111111-1111-1111-1111-111111111111");

    private readonly object _lock = new();
    private readonly HashSet<byte[]> _outstanding = new(ReferenceEqualityComparer.Instance);
    private readonly List<int> _producedFrames = new();

    // Hold gate: closed (Reset) freezes the producer mid-Produce after signalling _entered. Has a
    // safety timeout so a forgotten ReleaseHold can never deadlock a Dispose/Join.
    private readonly ManualResetEventSlim _hold = new(initialState: true);
    private readonly ManualResetEventSlim _entered = new(initialState: false);
    private const int HoldSafetyTimeoutMs = 2000;

    private int _totalFrames = 1_000_000;

    public int TotalFrames
    {
        get { lock (_lock) return _totalFrames; }
        set { lock (_lock) _totalFrames = value; }
    }

    /// <summary>Frame numbers for which <see cref="Produce"/> returns null (simulated torn read).</summary>
    public HashSet<int> NullFrames { get; } = new();

    /// <summary>When true, produced bundles carry an empty frame dictionary (no video layers).</summary>
    public bool EmptyFrames { get; set; }

    public int ProduceCount { get { lock (_lock) return _producedFrames.Count; } }
    public int OutstandingCount { get { lock (_lock) return _outstanding.Count; } }
    public int DoubleOrForeignReturns { get; private set; }
    public double LastScale { get; private set; }
    public bool Disposed { get; private set; }

    public bool HasProduced(int frame) { lock (_lock) return _producedFrames.Contains(frame); }

    // ---- Hold-gate controls (test thread) ----
    public void Hold() => _hold.Reset();
    public void ReleaseHold() => _hold.Set();
    public void ResetEntered() => _entered.Reset();
    public bool WaitEntered(int ms) => _entered.Wait(ms);

    public FrameBundle? Produce(int frame, int generation, double decodeScale)
    {
        // Guards the join-before-dispose ordering: the pump must never call Produce after Dispose.
        if (Disposed)
            throw new InvalidOperationException("Produce called after Dispose — ordering bug.");

        _entered.Set();
        _hold.Wait(HoldSafetyTimeoutMs);

        LastScale = decodeScale;

        lock (_lock)
        {
            _producedFrames.Add(frame);
            if (NullFrames.Contains(frame))
                return null;

            var frames = new Dictionary<Guid, VideoFrame>();
            if (!EmptyFrames)
            {
                var buffer = new byte[16];
                _outstanding.Add(buffer);
                frames[ClipId] = new VideoFrame(buffer, buffer.Length, 2, 2, 8, TimeSpan.FromSeconds(frame));
            }
            return new FrameBundle(frame, generation, Array.Empty<CompositionLayer>(), frames);
        }
    }

    public void Recycle(FrameBundle bundle)
    {
        lock (_lock)
        {
            foreach (VideoFrame vf in bundle.Frames.Values)
            {
                if (!_outstanding.Remove(vf.Buffer))
                    DoubleOrForeignReturns++;
            }
        }
        bundle.Frames.Clear(); // mirror ProjectBundleSource: guard against a double-recycle
    }

    public void Dispose() => Disposed = true;
}
