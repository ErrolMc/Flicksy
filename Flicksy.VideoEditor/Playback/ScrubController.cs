using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// Off-thread, coalesced timeline scrubbing (mirrors PostSnip's <c>VideoPlaybackOverlay</c>). While
/// paused, a ruler/timeline seek would otherwise decode synchronously on the UI thread — a ~120 ms
/// random-access seek per playhead change that freezes the app. <see cref="Request"/> hands the
/// target to a background worker, which decodes it via its own <see cref="ProjectBundleSource"/>
/// (own decoder cache, off the UI thread), then dispatches the pre-decoded <see cref="FrameBundle"/>
/// to the supplied present callback on the UI thread and recycles it. A capacity-1 DropOldest channel
/// coalesces a fast drag to its newest target, so the video catches the mouse within one seek
/// instead of replaying every position. Only the decode/seek runs off the UI thread; compositing
/// stays on it, in the callback.
/// <para>
/// Threading: <see cref="Request"/> is UI-thread; the decode runs on the worker; the present
/// callback is dispatched back to the captured (UI) dispatcher. The worker's decoder cache is
/// single-threaded by construction — only the worker calls <c>Produce</c> — and play/scrub are
/// time-exclusive, so it never races the playback pump's cache (ADR 0005's no-thrash rule holds).
/// <see cref="Dispose"/> joins the worker before tearing the cache down; the worker dispatches via
/// <see cref="Dispatcher.InvokeAsync(Action)"/> (never blocking on the dispatcher), so the join
/// can't deadlock even though Dispose runs on the UI thread.
/// </para>
/// </summary>
internal sealed class ScrubController : IDisposable
{
    private readonly record struct ScrubRequest(int Frame, double Scale);

    private readonly ProjectBundleSource _source;
    private readonly Channel<ScrubRequest> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private readonly Dispatcher _dispatcher;
    private readonly Action<FrameBundle> _present;

    /// <param name="present">
    /// Composites + presents one decoded bundle; invoked on the UI thread. The controller recycles
    /// the bundle after it returns, so the callback must not retain it.
    /// </param>
    public ScrubController(Project.Project project, Func<int> totalFrames, Action<FrameBundle> present)
    {
        _present = present;
        _dispatcher = Dispatcher.CurrentDispatcher; // constructed on the UI thread
        _source = new ProjectBundleSource(project, totalFrames);
        _channel = Channel.CreateBounded<ScrubRequest>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // free coalescing — newest target wins
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false, // keep the worker's continuation off the UI thread
        });
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Queue a scrub target (UI thread). Coalesces — only the newest survives a busy worker.</summary>
    public void Request(int frame, double decodeScale)
    {
        _channel.Writer.TryWrite(new ScrubRequest(frame, decodeScale));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (ScrubRequest req in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                FrameBundle? bundle = _source.Produce(req.Frame, 0, req.Scale);
                if (bundle is null)
                    continue; // torn read mid-edit; the next request re-decodes cleanly

                FrameBundle ready = bundle;
                // Fire-and-forget present (discarded, not awaited): the worker must not block on the
                // dispatcher, or Dispose's join could deadlock.
                _ = _dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        _present(ready);
                    }
                    finally
                    {
                        _source.Recycle(ready);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Dispose.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ScrubController worker failed: {ex}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Worker faulted/cancelled during teardown — nothing actionable.
        }
        _source.Dispose(); // safe now: worker joined, no Produce in flight
        _cts.Dispose();
    }
}
