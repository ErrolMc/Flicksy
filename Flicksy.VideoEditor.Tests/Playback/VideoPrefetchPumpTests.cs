using System;
using System.Threading;
using System.Threading.Tasks;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Playback;
using Flicksy.VideoEditor.Project;
using NUnit.Framework;

namespace Flicksy.VideoEditor.Tests.Playback;

/// <summary>
/// Drives the real <see cref="VideoPrefetchPump"/> producer thread against a <see cref="FakeBundleSource"/>
/// (no FFmpeg/Skia/WPF) to verify the queue / drift / seek+generation / scale / lifetime logic. Every
/// test ends in <see cref="TearDown"/> with a buffer-leak and double-return assertion — the core
/// correctness invariant of the cross-thread buffer baton.
/// </summary>
[TestFixture]
public class VideoPrefetchPumpTests
{
    private const int Timeout = 2000;

    private FakeBundleSource _fake = null!;
    private VideoPrefetchPump _pump = null!;

    // resyncThreshold defaults effectively-infinite so the producer decodes purely sequentially in
    // the tests that don't exercise resync; the two resync tests pass a small explicit value.
    private VideoPrefetchPump CreatePump(int depth = 12, int totalFrames = 1_000_000, int resyncThreshold = int.MaxValue)
    {
        _fake = new FakeBundleSource { TotalFrames = totalFrames };
        _pump = new VideoPrefetchPump(_fake, resyncThresholdFrames: resyncThreshold, depth: depth);
        return _pump;
    }

    [TearDown]
    public void TearDown()
    {
        if (_pump is null)
            return;
        _fake.ReleaseHold();        // let any held producer finish so Dispose can join
        _pump.Dispose();            // joins the producer, drains the queue, disposes the source
        Assert.That(_fake.OutstandingCount, Is.EqualTo(0), "buffer leak: outstanding != 0 after dispose");
        Assert.That(_fake.DoubleOrForeignReturns, Is.EqualTo(0), "double/foreign buffer return detected");
        Assert.That(_fake.Disposed, Is.True, "pump.Dispose did not dispose the source");
    }

    private static bool Wait(Func<bool> condition) => SpinWait.SpinUntil(condition, Timeout);

    // ---- Queue ----

    [Test]
    public void Start_PrimesQueueUpToDepth_AndStops()
    {
        VideoPrefetchPump pump = CreatePump(depth: 4);
        pump.Start(0);
        Assert.That(Wait(() => _fake.ProduceCount >= 4), Is.True, "did not prime to depth");
        Thread.Sleep(50); // give any (incorrect) over-production a chance to show
        Assert.That(_fake.ProduceCount, Is.EqualTo(4), "produced past the queue depth");
    }

    [Test]
    public void BeginFrame_FrontMatches_ReturnsTrue()
    {
        VideoPrefetchPump pump = CreatePump(totalFrames: 1);
        pump.Start(0);
        Assert.That(Wait(() => _fake.HasProduced(0)), Is.True);
        Assert.That(pump.BeginFrame(0, 1.0), Is.True);
        pump.EndFrame();
    }

    [Test]
    public void Acquire_ReturnsDecodedFrameForClip_NullForUnknown()
    {
        VideoPrefetchPump pump = CreatePump(totalFrames: 1);
        pump.Start(0);
        Assert.That(Wait(() => _fake.HasProduced(0)), Is.True);
        Assert.That(pump.BeginFrame(0, 1.0), Is.True);

        VideoFrame? frame = pump.Acquire(new MediaClip { Id = FakeBundleSource.ClipId }, TimeSpan.Zero, 1.0);
        Assert.That(frame, Is.Not.Null);
        Assert.That(frame!.Value.Pts, Is.EqualTo(TimeSpan.FromSeconds(0)));

        VideoFrame? unknown = pump.Acquire(new MediaClip { Id = Guid.NewGuid() }, TimeSpan.Zero, 1.0);
        Assert.That(unknown, Is.Null, "unknown clip should miss");

        pump.EndFrame();
    }

    [Test]
    public void BeginFrame_OnlyFutureFrames_ReturnsFalse()
    {
        // Producer starts at 5, so the queue holds only frames > 2; asking for 2 must miss —
        // best-effort presents the newest frame AT OR BEFORE the playhead, and there is none.
        VideoPrefetchPump pump = CreatePump(totalFrames: 56);
        pump.Start(5);
        Assert.That(Wait(() => _fake.HasProduced(5)), Is.True);
        Assert.That(pump.BeginFrame(2, 1.0), Is.False, "future-only queue should hold the previous frame");
    }

    [Test]
    public void HasReadyFrameAt_TrueOnlyWhenFrameAtOrBeforePlayheadQueued()
    {
        // Backs the engine's startup prebuffer: it's only "ready" once a frame at or before the
        // playhead is decoded — so play holds the clock until the first frame lands instead of
        // racing the playhead ahead of a cold decoder.
        VideoPrefetchPump pump = CreatePump(totalFrames: 56);
        Assert.That(pump.HasReadyFrameAt(0), Is.False, "nothing produced yet → not ready");

        pump.Start(5); // producer makes 5, 6, 7, ...
        Assert.That(Wait(() => _fake.HasProduced(5)), Is.True);

        Assert.That(pump.HasReadyFrameAt(2), Is.False, "queue front (5) is after the playhead (2) → not ready");
        Assert.That(pump.HasReadyFrameAt(5), Is.True, "frame 5 is queued at or before the playhead → ready");
    }

    [Test]
    public void EndFrame_ReturnsCurrentBuffer()
    {
        VideoPrefetchPump pump = CreatePump(totalFrames: 1);
        pump.Start(0);
        Assert.That(Wait(() => _fake.HasProduced(0)), Is.True);
        Assert.That(pump.BeginFrame(0, 1.0), Is.True);
        Assert.That(_fake.OutstandingCount, Is.EqualTo(1), "claimed frame should still be outstanding");
        pump.EndFrame();
        Assert.That(_fake.OutstandingCount, Is.EqualTo(0), "EndFrame should return the buffer");
    }

    // ---- Drift (framerate > tick rate) ----

    [Test]
    public void BeginFrame_SkipsIntermediateFrames_ReturningTheirBuffers()
    {
        VideoPrefetchPump pump = CreatePump(depth: 12, totalFrames: 6); // 0..5
        pump.Start(0);
        Assert.That(Wait(() => _fake.ProduceCount >= 6), Is.True);
        Assert.That(_fake.OutstandingCount, Is.EqualTo(6));

        Assert.That(pump.BeginFrame(3, 1.0), Is.True, "should skip to frame 3");
        Assert.That(_fake.OutstandingCount, Is.EqualTo(3), "dropped 0,1,2 should be returned (3 current + 4,5 queued)");
        pump.EndFrame();
        Assert.That(_fake.OutstandingCount, Is.EqualTo(2), "frames 4,5 remain");
    }

    [Test]
    public void BeginFrame_PastAllQueued_ShowsNewestAvailable()
    {
        // Producer far behind the playhead (only 0..3 exist, asking for 10): present the newest
        // available frame (3) rather than freeze, recycling the older ones.
        VideoPrefetchPump pump = CreatePump(depth: 12, totalFrames: 4); // 0..3
        pump.Start(0);
        Assert.That(Wait(() => _fake.ProduceCount >= 4), Is.True);
        Assert.That(pump.BeginFrame(10, 1.0), Is.True, "should present the newest available frame");
        VideoFrame? frame = pump.Acquire(new MediaClip { Id = FakeBundleSource.ClipId }, TimeSpan.Zero, 1.0);
        Assert.That(frame!.Value.Pts, Is.EqualTo(TimeSpan.FromSeconds(3)), "newest available is frame 3");
        Assert.That(_fake.OutstandingCount, Is.EqualTo(1), "older frames recycled; only the chosen one outstanding");
        pump.EndFrame();
    }

    [Test]
    public void Producer_ResyncsToPlayhead_WhenItTrailsBeyondThreshold()
    {
        // While the producer is stuck decoding frame 0, the playhead races to 100 — far past the
        // resync threshold (30). Once it frees, the producer must jump to the playhead in ONE seek
        // (skipping 1..99), not grind through them: bounded resync keeps a sub-realtime decoder from
        // drifting unboundedly behind the open-loop audio.
        VideoPrefetchPump pump = CreatePump(resyncThreshold: 30);
        _fake.Hold();
        _fake.ResetEntered();
        pump.Start(0);
        Assert.That(_fake.WaitEntered(Timeout), Is.True, "producer did not enter Produce(0)");

        Assert.That(pump.BeginFrame(100, 1.0), Is.False, "nothing ready yet → miss");
        _fake.ReleaseHold();

        Assert.That(Wait(() => _fake.HasProduced(100)), Is.True, "producer did not resync to the playhead");
        Assert.That(_fake.HasProduced(50), Is.False, "producer should have dropped the backlog, not decoded it");
    }

    [Test]
    public void Producer_DecodesSequentially_WhenWithinThreshold()
    {
        // Playhead only a few frames ahead of the producer (well within the resync threshold): the
        // producer must NOT jump — it decodes every intermediate frame, so a fast-enough clip plays
        // every frame rather than skipping. This is the other half of bounded resync.
        VideoPrefetchPump pump = CreatePump(resyncThreshold: 30);
        _fake.Hold();
        _fake.ResetEntered();
        pump.Start(0);
        Assert.That(_fake.WaitEntered(Timeout), Is.True, "producer did not enter Produce(0)");

        Assert.That(pump.BeginFrame(5, 1.0), Is.False, "nothing ready yet → miss");
        _fake.ReleaseHold();

        Assert.That(Wait(() => _fake.HasProduced(5)), Is.True, "producer did not reach the playhead");
        Assert.That(_fake.HasProduced(3), Is.True, "within threshold the producer must decode every frame, not skip");
    }

    // ---- Seek + generation ----

    [Test]
    public void SeekForward_FlushesQueue_ReturnsAllBuffers()
    {
        VideoPrefetchPump pump = CreatePump(depth: 12, totalFrames: 4); // 0..3, then target 100 is past end → no reprime
        pump.Start(0);
        Assert.That(Wait(() => _fake.ProduceCount >= 4), Is.True);
        pump.Reprime(100, 1.0);
        Assert.That(Wait(() => _fake.OutstandingCount == 0), Is.True, "seek did not flush all buffers");
    }

    [Test]
    public void SeekBackward_ServesNewFrame_NotStale()
    {
        VideoPrefetchPump pump = CreatePump(depth: 12, totalFrames: 56);
        pump.Start(50);
        Assert.That(Wait(() => _fake.HasProduced(55)), Is.True, "did not prime 50..55");
        pump.Reprime(10, 1.0);
        Assert.That(Wait(() => _fake.HasProduced(10)), Is.True, "did not reprime from 10");

        Assert.That(pump.BeginFrame(10, 1.0), Is.True, "frame 10 should be served after backward seek");
        VideoFrame? frame = pump.Acquire(new MediaClip { Id = FakeBundleSource.ClipId }, TimeSpan.Zero, 1.0);
        Assert.That(frame!.Value.Pts, Is.EqualTo(TimeSpan.FromSeconds(10)), "served a stale pre-seek frame");
        pump.EndFrame();
    }

    [Test]
    public void SeekDuringInFlightDecode_DiscardsInFlight_NoLeak()
    {
        VideoPrefetchPump pump = CreatePump();
        _fake.Hold();          // freeze the producer mid-Produce
        _fake.ResetEntered();
        pump.Start(0);
        Assert.That(_fake.WaitEntered(Timeout), Is.True, "producer did not enter Produce");

        pump.Reprime(100, 1.0); // bump generation while frame 0's decode is in flight
        _fake.ReleaseHold();   // frame 0 completes → stale generation → discarded, not enqueued

        Assert.That(Wait(() => _fake.HasProduced(100)), Is.True, "did not reprime after seek");
        // No-leak / no-double-return verified in TearDown — proves the in-flight bundle was recycled once.
    }

    [Test]
    public void RapidSeeks_NoLeakNoDoubleReturn()
    {
        VideoPrefetchPump pump = CreatePump(depth: 8, totalFrames: 10_000);
        pump.Start(0);

        var rng = new Random(12345);
        for (int i = 0; i < 1000; i++)
        {
            int target = rng.Next(0, 9000);
            pump.Reprime(target, 1.0);
            double scale = (i % 50 == 0) ? 0.5 : 1.0; // occasionally churn scale too
            if (pump.BeginFrame(target, scale))
                pump.EndFrame();
        }
        // TearDown asserts the stress left no leak and no double-return.
    }

    // ---- Scale ----

    [Test]
    public void ScaleChange_Misses_ThenReprimesAtNewScale()
    {
        VideoPrefetchPump pump = CreatePump();
        pump.Start(0);
        Assert.That(Wait(() => _fake.ProduceCount >= 1), Is.True);
        Assert.That(pump.BeginFrame(0, 0.5), Is.False, "a scale change should miss and reprime");
        Assert.That(Wait(() => _fake.LastScale == 0.5), Is.True, "producer did not reprime at the new scale");
    }

    // ---- Prefetch (paused-state warm-up) ----

    [Test]
    public void IsRunning_ReflectsLifecycle()
    {
        VideoPrefetchPump pump = CreatePump();
        Assert.That(pump.IsRunning, Is.False, "not running before Start");
        pump.Start(0);
        Assert.That(pump.IsRunning, Is.True, "running after Start");
        pump.Stop();
        Assert.That(pump.IsRunning, Is.False, "not running after Stop");
    }

    [Test]
    public void Prefetch_StartsWhenIdle_RepositionsWhenRunning()
    {
        VideoPrefetchPump pump = CreatePump(totalFrames: 200);
        Assert.That(pump.IsRunning, Is.False);

        pump.Prefetch(10, 0.5); // idle → starts at the given frame + scale
        Assert.That(Wait(() => _fake.HasProduced(10)), Is.True, "Prefetch should start the producer at the frame");
        Assert.That(Wait(() => _fake.LastScale == 0.5), Is.True, "Prefetch should decode at the given scale");
        Assert.That(pump.IsRunning, Is.True);

        pump.Prefetch(100, 0.5); // running → reprimes to the new position
        Assert.That(Wait(() => _fake.HasProduced(100)), Is.True, "Prefetch should reposition a running producer");
    }

    // ---- Lifetime ----

    [Test]
    public void Dispose_StopsProducer_ReturnsAllBuffers()
    {
        VideoPrefetchPump pump = CreatePump(depth: 8);
        pump.Start(0);
        Assert.That(Wait(() => _fake.ProduceCount >= 8), Is.True);

        pump.Dispose();
        int after = _fake.ProduceCount;
        Thread.Sleep(50);
        Assert.That(_fake.ProduceCount, Is.EqualTo(after), "producer kept running after Dispose");
        Assert.That(_fake.OutstandingCount, Is.EqualTo(0));
        Assert.That(_fake.Disposed, Is.True);
    }

    [Test]
    public void Dispose_DuringInFlightDecode_DoesNotThrow()
    {
        VideoPrefetchPump pump = CreatePump();
        _fake.Hold();
        _fake.ResetEntered();
        pump.Start(0);
        Assert.That(_fake.WaitEntered(Timeout), Is.True);

        Task disposeTask = Task.Run(() => pump.Dispose());
        _fake.ReleaseHold(); // let the in-flight Produce finish so the join can complete

        Assert.That(disposeTask.Wait(Timeout), Is.True, "Dispose did not complete — join hang?");
        Assert.That(disposeTask.Exception, Is.Null, "Dispose threw (likely source disposed before join)");
        Assert.That(_fake.Disposed, Is.True);
    }

    [Test]
    public void EndFrame_IsIdempotent_AndBeginFrameReclaimsSkippedCurrent()
    {
        VideoPrefetchPump pump = CreatePump();
        pump.Start(0);
        Assert.That(Wait(() => _fake.HasProduced(0)), Is.True);
        Assert.That(pump.BeginFrame(0, 1.0), Is.True);

        // Skip EndFrame for frame 0; the next BeginFrame must reclaim it (no leak).
        Assert.That(Wait(() => _fake.HasProduced(1)), Is.True);
        Assert.That(pump.BeginFrame(1, 1.0), Is.True);
        pump.EndFrame();
        pump.EndFrame(); // idempotent — no throw, no double-return
    }

    // ---- End / empty / torn ----

    [Test]
    public void Producer_StopsAtTotalFrames()
    {
        VideoPrefetchPump pump = CreatePump(depth: 12, totalFrames: 3); // only 0,1,2
        pump.Start(0);
        Assert.That(Wait(() => _fake.ProduceCount >= 3), Is.True);
        Thread.Sleep(50);
        Assert.That(_fake.ProduceCount, Is.EqualTo(3), "produced past TotalFrames");
    }

    [Test]
    public void EmptyBundles_NoVideoLayers_NoLeak()
    {
        VideoPrefetchPump pump = CreatePump(totalFrames: 5);
        _fake.EmptyFrames = true;
        pump.Start(0);
        Assert.That(Wait(() => _fake.HasProduced(0)), Is.True);
        Assert.That(pump.BeginFrame(0, 1.0), Is.True);
        VideoFrame? frame = pump.Acquire(new MediaClip { Id = FakeBundleSource.ClipId }, TimeSpan.Zero, 1.0);
        Assert.That(frame, Is.Null, "an empty bundle has no frames");
        pump.EndFrame();
    }

    [Test]
    public void TornRead_NullProduce_FrameSkipped_NewestBeforeItShown()
    {
        // Frame 2 is a torn read (Produce → null), never enqueued. Asking for 2 falls back to the
        // newest available before it (frame 1); frame 3 is served exactly.
        VideoPrefetchPump pump = CreatePump(depth: 12, totalFrames: 5);
        _fake.NullFrames.Add(2);
        pump.Start(0);
        Assert.That(Wait(() => _fake.HasProduced(4)), Is.True);

        Assert.That(pump.BeginFrame(2, 1.0), Is.True, "should fall back to the newest frame before the torn one");
        VideoFrame? atTwo = pump.Acquire(new MediaClip { Id = FakeBundleSource.ClipId }, TimeSpan.Zero, 1.0);
        Assert.That(atTwo!.Value.Pts, Is.EqualTo(TimeSpan.FromSeconds(1)), "torn frame 2 → newest available is frame 1");
        pump.EndFrame();

        Assert.That(pump.BeginFrame(3, 1.0), Is.True, "frame 3 is available exactly");
        VideoFrame? atThree = pump.Acquire(new MediaClip { Id = FakeBundleSource.ClipId }, TimeSpan.Zero, 1.0);
        Assert.That(atThree!.Value.Pts, Is.EqualTo(TimeSpan.FromSeconds(3)));
        pump.EndFrame();
    }
}
