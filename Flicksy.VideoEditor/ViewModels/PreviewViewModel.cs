using System;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.Playback;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// State for the Preview surface. Drives the composited image displayed in
/// <c>PreviewView</c>: subscribes to <see cref="TransportViewModel.Playhead"/> and the
/// project's resolution settings, and on each change asks <see cref="ICompositor"/> to
/// repaint its reusable target bitmap. <see cref="CurrentFrame"/> is that bitmap — owned
/// here, reused across frames, and reallocated only when the project resolution or preview
/// quality changes (the caller-owned contract from ADR 0004 that keeps the compositor from allocating
/// ~8 MB per frame). The view's <c>&lt;Image Source="…"&gt;</c> binds to it; in-place
/// repaints surface through <c>WriteableBitmap</c>'s own invalidation, so the binding only
/// changes on a resolution swap.
/// <para>
/// Threading: every PropertyChanged path that triggers <see cref="Render"/> originates on
/// the UI thread (Transport commands incl. the playback engine's clock tick, Settings edits
/// via the inspector), so the call into <c>SkiaCompositor</c> stays on the UI thread —
/// required both for the <c>GraphicsClip</c> render path that bounces through
/// <c>RenderTargetBitmap</c> and for the unfrozen reusable bitmap, which can't cross threads.
/// During playback the per-frame video <em>decode</em> is supplied by an off-thread pump (the
/// engine sets <see cref="PlaybackFrames"/>); the compositing call itself still runs here on the UI
/// thread, so those two constraints hold (ADR 0009).
/// </para>
/// </summary>
public partial class PreviewViewModel : ObservableObject, IPlaybackFrameSink, IDisposable
{
    private readonly Project.Project _project;
    private readonly TransportViewModel _transport;
    private readonly ICompositor _compositor;

    // Off-thread, coalesced scrubbing: a paused ruler/timeline seek decodes on a background thread
    // (~120 ms random-access seek) instead of blocking the UI; the worker calls PresentScrubFrame
    // back on the UI thread to composite (~0.5 ms). See ScrubController.
    private readonly ScrubController _scrub;
    private bool _disposed;

    // The reusable composite target. Recreated only when the project resolution changes;
    // every other frame paints into this same instance. CurrentFrame points at it.
    private WriteableBitmap? _target;

    /// <summary>
    /// The most recently composited frame. <see cref="ImageSource"/> rather than
    /// <c>WriteableBitmap</c> so future backends could supply a different concrete type
    /// without touching the binding.
    /// </summary>
    [ObservableProperty]
    private ImageSource? currentFrame;

    /// <summary>
    /// Preview render fidelity (view-only; never serialized, never affects export). Lowering it
    /// shrinks the compositor's target bitmap for cheaper playback/scrubbing. See ADR 0008.
    /// </summary>
    [ObservableProperty]
    private PreviewQuality selectedQuality = PreviewQuality.Full;

    private IPlaybackFrameSource? _playbackFrames;

    /// <summary>
    /// Off-thread decode source during playback (ADR 0009), set by <see cref="Playback.PlaybackEngine"/>
    /// on play and cleared on pause. When non-null, <see cref="Render"/> composites from pre-decoded
    /// frames instead of decoding synchronously on the UI thread; when null, a paused playhead change
    /// scrubs off-thread via <see cref="ScrubController"/> and other repaints use the synchronous path
    /// (static preview). Not an <c>[ObservableProperty]</c> — nothing in the UI binds to it.
    /// <para>
    /// Clearing it to null re-renders immediately via the synchronous static path so the displayed frame is
    /// correct the instant playback stops — in particular a seek-while-playing (the engine pauses, which
    /// clears this) repaints the seeked frame at once instead of leaving the stale, still-positioned pump's
    /// missed frame on screen until the next Play. Setting it to the pump deliberately does NOT render here:
    /// Play's prime-hold + clock drive the first frame (and the paused scrub already left it on screen), and
    /// a render now would consume the buffered start frame (BeginFrame/EndFrame), emptying the slot that
    /// Play's <c>HasReadyFrameAt</c> gate waits on and stalling the start by the full prime timeout.
    /// </para>
    /// </summary>
    public IPlaybackFrameSource? PlaybackFrames
    {
        get => _playbackFrames;
        set
        {
            _playbackFrames = value;
            // Repaint only on a clear (pause / seek-induced pause / dispose) — see remarks for why
            // setting it to the pump must not render here.
            if (value is null)
            {
                Render();
            }
        }
    }

    /// <summary>
    /// The decode scale the preview currently renders at — matches the <c>decodeScale</c> passed to
    /// the pump in <see cref="Render"/> (target pixel width ÷ project resolution width, i.e. the
    /// selected quality). The engine reads it to warm the paused prefetch at the right scale.
    /// </summary>
    public double CurrentDecodeScale
    {
        get
        {
            WriteableBitmap? target = _target;
            int width = ProjectSettings.ResolutionWidth;
            return target is not null && width > 0
                ? (double)target.PixelWidth / width
                : 1.0;
        }
    }

    public PreviewViewModel(Project.Project project, TransportViewModel transport, ICompositor compositor)
    {
        _project = project;
        _transport = transport;
        _compositor = compositor;
        ProjectSettings = project.Settings;

        _transport.PropertyChanged += OnTransportPropertyChanged;
        _project.Settings.PropertyChanged += OnSettingsPropertyChanged;

        _scrub = new ScrubController(project, () => _transport.TotalFrames, PresentScrubFrame);

        // Render once so CurrentFrame is non-null at construction — the view's
        // Stretch=Uniform needs a sized source to letterbox correctly even on an empty
        // project (SkiaCompositor clears to black, so the first frame is a black fill at
        // project resolution).
        Render();
    }

    public ProjectSettings ProjectSettings { get; }

    /// <summary>Options for the preview-quality dropdown, full fidelity first.</summary>
    public IReadOnlyList<PreviewQualityOption> QualityOptions { get; } = new[]
    {
        new PreviewQualityOption { Label = "Full", Quality = PreviewQuality.Full },
        new PreviewQualityOption { Label = "1/2", Quality = PreviewQuality.Half },
        new PreviewQualityOption { Label = "1/4", Quality = PreviewQuality.Quarter },
        new PreviewQualityOption { Label = "1/8", Quality = PreviewQuality.Eighth },
    };

    private void OnTransportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransportViewModel.Playhead))
        {
            // A playhead change is either a playback tick (handled by the pump path) or a user
            // seek/scrub while paused (routed to the off-thread scrub worker).
            Render(playheadChange: true);
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettings.ResolutionWidth)
            || e.PropertyName == nameof(ProjectSettings.ResolutionHeight))
        {
            Render();
        }
    }

    // Generated hook for [ObservableProperty] selectedQuality: a new quality resizes the
    // target bitmap (EnsureTarget) and repaints at the new scale.
    partial void OnSelectedQualityChanged(PreviewQuality value) => Render();

    private void Render(bool playheadChange = false)
    {
        try
        {
            WriteableBitmap? target = EnsureTarget();
            if (target is null)
                return;

            int frame = _transport.Playhead;
            IPlaybackFrameSource? frames = PlaybackFrames;
            if (frames is not null)
            {
                // Playback: composite from the off-thread pump's pre-decoded frames (ADR 0009).
                // The decode scale matches what the compositor derives internally (target/project).
                double decodeScale = (double)target.PixelWidth / ProjectSettings.ResolutionWidth;
                if (!frames.BeginFrame(frame, decodeScale))
                    return; // miss → keep the previous frame

                try
                {
                    _compositor.RenderFrame(_project, frame, target, frames, frames.CurrentLayers);
                }
                finally
                {
                    frames.EndFrame();
                }
            }
            else if (playheadChange)
            {
                // Active scrub while paused: hand the target to the background ScrubController, which
                // decodes off the UI thread and calls PresentScrubFrame back here. The UI thread does
                // no decode, so it never freezes mid-drag (the PostSnip behaviour).
                double scrubScale = (double)target.PixelWidth / ProjectSettings.ResolutionWidth;
                _scrub.Request(frame, scrubScale);
            }
            else
            {
                // Initial / static / resolution / quality / settle-after-pause: a single, infrequent
                // synchronous decode on the UI thread (the canonical path). Not a drag, so the
                // on-thread decode is acceptable, and it keeps CurrentFrame painted synchronously at
                // construction.
                _compositor.RenderFrame(_project, frame, target);
            }
        }
        catch (Exception ex)
        {
            // Compositor failures shouldn't crash the editor — log and leave the
            // previous frame on-screen. Production-grade surfacing lands with #11's
            // playback loop, which needs a proper status channel anyway.
            System.Diagnostics.Debug.WriteLine($"PreviewViewModel.Render failed: {ex}");
        }
    }

    /// <summary>
    /// Composite a worker-decoded scrub <paramref name="bundle"/> into the reusable target and
    /// present it — the same cheap (~0.5 ms) paint the playback path does, just sourced from the
    /// off-thread scrub decode instead of the pump. The <see cref="ScrubController"/> invokes this on
    /// the UI thread and recycles the bundle afterwards.
    /// </summary>
    private void PresentScrubFrame(FrameBundle bundle)
    {
        // Playback may have taken over (or the VM disposed) between decode and this dispatch — drop
        // the stale scrub frame rather than paint over the live one. (Controller still recycles it.)
        if (_disposed || PlaybackFrames is not null)
            return;

        try
        {
            WriteableBitmap? target = EnsureTarget();
            if (target is null)
                return;

            // No size guard needed (unlike a raw copy): PaintMediaClip maps the decoded frame onto
            // the native source extent, so a bundle decoded at a now-stale scale still composites
            // correctly, just at that fidelity for one frame.
            var provider = new BundleFrameProvider(bundle);
            _compositor.RenderFrame(_project, bundle.Frame, target, provider, bundle.Layers);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PreviewViewModel.PresentScrubFrame failed: {ex}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _transport.PropertyChanged -= OnTransportPropertyChanged;
        _project.Settings.PropertyChanged -= OnSettingsPropertyChanged;

        // Joins the scrub worker before tearing down its decoder cache. _disposed is set first, so a
        // present callback already queued on the dispatcher no-ops instead of touching the compositor.
        _scrub.Dispose();
    }

    /// <summary>
    /// Returns the reusable target bitmap, (re)creating it when absent or when its size
    /// changes (project resolution or <see cref="SelectedQuality"/>), and pointing
    /// <see cref="CurrentFrame"/> at the new instance. The target is sized at project
    /// resolution times the selected quality's scale (proxy mode, ADR 0008); the compositor
    /// derives its render scale from that size. Returns null for a degenerate resolution
    /// (≤ 0 on either axis), in which case the caller skips rendering. Reusing one bitmap
    /// across frames is the whole point of the caller-owned contract — it removes the
    /// per-frame ~8 MB allocation.
    /// </summary>
    private WriteableBitmap? EnsureTarget()
    {
        int projectWidth = ProjectSettings.ResolutionWidth;
        int projectHeight = ProjectSettings.ResolutionHeight;

        if (projectWidth <= 0 || projectHeight <= 0)
        {
            _target = null;
            CurrentFrame = null;
            return null;
        }

        // Size the target below project resolution per the selected quality. PreviewView's
        // Stretch=Uniform scales the smaller bitmap back up to the same on-screen size, so a
        // lower quality reads as the same picture at reduced fidelity.
        double scale = SelectedQuality.Scale();
        int w = Math.Max(1, (int)Math.Round(projectWidth * scale));
        int h = Math.Max(1, (int)Math.Round(projectHeight * scale));

        if (_target is { } existing && existing.PixelWidth == w && existing.PixelHeight == h)
        {
            return existing;
        }

        _target = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
        CurrentFrame = _target;
        return _target;
    }
}
