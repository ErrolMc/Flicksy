using System;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// State for the Preview surface. Drives the composited image displayed in
/// <c>PreviewView</c>: subscribes to <see cref="TransportViewModel.Playhead"/> and the
/// project's resolution settings, and on each change asks <see cref="ICompositor"/> to
/// repaint its reusable target bitmap. <see cref="CurrentFrame"/> is that bitmap — owned
/// here, reused across frames, and reallocated only when the project resolution changes
/// (the caller-owned contract from ADR 0004 that keeps the compositor from allocating
/// ~8 MB per frame). The view's <c>&lt;Image Source="…"&gt;</c> binds to it; in-place
/// repaints surface through <c>WriteableBitmap</c>'s own invalidation, so the binding only
/// changes on a resolution swap.
/// <para>
/// Threading: every PropertyChanged path that triggers <see cref="Render"/> originates on
/// the UI thread (Transport commands incl. the playback engine's clock tick, Settings edits
/// via the inspector), so the call into <c>SkiaCompositor</c> stays on the UI thread —
/// required both for the <c>GraphicsClip</c> render path that bounces through
/// <c>RenderTargetBitmap</c> and for the unfrozen reusable bitmap, which can't cross threads.
/// Off-thread decode-ahead is deferred phase 2 (ADR 0005).
/// </para>
/// </summary>
public partial class PreviewViewModel : ObservableObject
{
    private readonly Project.Project _project;
    private readonly TransportViewModel _transport;
    private readonly ICompositor _compositor;

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

    public PreviewViewModel(Project.Project project, TransportViewModel transport, ICompositor compositor)
    {
        _project = project;
        _transport = transport;
        _compositor = compositor;
        ProjectSettings = project.Settings;

        _transport.PropertyChanged += OnTransportPropertyChanged;
        _project.Settings.PropertyChanged += OnSettingsPropertyChanged;

        // Render once so CurrentFrame is non-null at construction — the view's
        // Stretch=Uniform needs a sized source to letterbox correctly even on an empty
        // project (SkiaCompositor clears to black, so the first frame is a black fill at
        // project resolution).
        Render();
    }

    public ProjectSettings ProjectSettings { get; }

    private void OnTransportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransportViewModel.Playhead))
        {
            Render();
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

    private void Render()
    {
        try
        {
            var target = EnsureTarget();
            if (target is null) return;
            _compositor.RenderFrame(_project, _transport.Playhead, target);
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
    /// Returns the reusable target bitmap, (re)creating it when absent or when the project
    /// resolution changed, and pointing <see cref="CurrentFrame"/> at the new instance.
    /// Returns null for a degenerate resolution (≤ 0 on either axis), in which case the
    /// caller skips rendering. Reusing one bitmap across frames is the whole point of the
    /// caller-owned contract — it removes the per-frame ~8 MB allocation.
    /// </summary>
    private WriteableBitmap? EnsureTarget()
    {
        int w = ProjectSettings.ResolutionWidth;
        int h = ProjectSettings.ResolutionHeight;

        if (w <= 0 || h <= 0)
        {
            _target = null;
            CurrentFrame = null;
            return null;
        }

        if (_target is { } existing && existing.PixelWidth == w && existing.PixelHeight == h)
        {
            return existing;
        }

        _target = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
        CurrentFrame = _target;
        return _target;
    }
}
