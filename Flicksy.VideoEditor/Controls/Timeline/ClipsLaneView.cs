using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Controls.Timeline;

/// <summary>
/// Renders one <see cref="ClipView"/> per <see cref="Project.Track.Clips"/> entry, positioned
/// on a <see cref="Canvas"/> at <c>TimelineStart × PixelsPerFrame</c> with width
/// <c>Duration × PixelsPerFrame</c>. Both inputs come in as DPs (<see cref="Track"/>,
/// <see cref="Timeline"/>) so the lane's parent <c>ItemTemplate</c> binds them explicitly —
/// no visual-tree walking. The lane subscribes to:
///  - <c>Track.Clips.CollectionChanged</c> — add/remove rebuilds children.
///  - Each <c>Clip.PropertyChanged</c> — <c>TimelineStart</c>/<c>Duration</c> re-layout.
///  - <c>Timeline.PropertyChanged</c> — <c>PixelsPerFrame</c> re-layouts all clips.
///  - <c>Timeline.SelectedClips.CollectionChanged</c> — re-applies
///    <see cref="ClipView.IsSelected"/> across the lane so every clip in the multi-selection
///    set is highlighted, not just the primary <see cref="TimelineViewModel.SelectedClip"/>.
/// </summary>
public sealed class ClipsLaneView : Canvas
{
    public const double TrackHeight = 56;
    private const double ClipVerticalPadding = 4;
    private const double MinClipWidth = 2;

    // Two frozen brushes for cheap per-frame swaps during drag. Refused state tints the
    // lane red so the user sees the no-drop target without depending only on the cursor.
    private static readonly SolidColorBrush DefaultLaneBackground = CreateFrozenBrush(0x14, 0x14, 0x14);
    private static readonly SolidColorBrush RefusedLaneBackground = CreateFrozenBrush(0x32, 0x14, 0x14);

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track),
        typeof(Track),
        typeof(ClipsLaneView),
        new PropertyMetadata(null, OnTrackChanged));

    public static readonly DependencyProperty TimelineProperty = DependencyProperty.Register(
        nameof(Timeline),
        typeof(TimelineViewModel),
        typeof(ClipsLaneView),
        new PropertyMetadata(null, OnTimelineChanged));

    private readonly Dictionary<Clip, ClipView> _clipViews = new();
    private readonly PropertyChangedEventHandler _clipHandler;
    private readonly PropertyChangedEventHandler _timelineHandler;
    private readonly PropertyChangedEventHandler _transportHandler;
    private readonly NotifyCollectionChangedEventHandler _clipsChangedHandler;
    private readonly NotifyCollectionChangedEventHandler _selectedClipsChangedHandler;
    private TransportViewModel? _subscribedTransport;
    private GhostClipAdorner? _ghostAdorner;

    public ClipsLaneView()
    {
        Background = DefaultLaneBackground;
        Height = TrackHeight;
        ClipToBounds = false;

        _clipHandler = OnClipPropertyChanged;
        _timelineHandler = OnTimelinePropertyChanged;
        _transportHandler = OnTransportPropertyChanged;
        _clipsChangedHandler = OnClipsCollectionChanged;
        _selectedClipsChangedHandler = OnSelectedClipsCollectionChanged;

        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
    }

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public Track? Track
    {
        get => (Track?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public TimelineViewModel? Timeline
    {
        get => (TimelineViewModel?)GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    private static void OnTrackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ClipsLaneView)d).HandleTrackChanged((Track?)e.OldValue, (Track?)e.NewValue);
    }

    private static void OnTimelineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ClipsLaneView)d).HandleTimelineChanged((TimelineViewModel?)e.OldValue, (TimelineViewModel?)e.NewValue);
    }

    private void HandleTrackChanged(Track? oldTrack, Track? newTrack)
    {
        if (oldTrack is not null)
        {
            oldTrack.Clips.CollectionChanged -= _clipsChangedHandler;
        }
        if (newTrack is not null)
        {
            newTrack.Clips.CollectionChanged += _clipsChangedHandler;
        }
        RebuildClips();
    }

    private void HandleTimelineChanged(TimelineViewModel? oldVm, TimelineViewModel? newVm)
    {
        if (oldVm is not null)
        {
            oldVm.PropertyChanged -= _timelineHandler;
            oldVm.SelectedClips.CollectionChanged -= _selectedClipsChangedHandler;
        }
        if (_subscribedTransport is not null)
        {
            _subscribedTransport.PropertyChanged -= _transportHandler;
        }
        if (newVm is not null)
        {
            newVm.PropertyChanged += _timelineHandler;
            newVm.SelectedClips.CollectionChanged += _selectedClipsChangedHandler;
        }
        _subscribedTransport = newVm?.Transport;
        if (_subscribedTransport is not null)
        {
            _subscribedTransport.PropertyChanged += _transportHandler;
        }
        UpdateAllLayouts();
        UpdateAllSelections();
        InvalidateMeasure();
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.PixelsPerFrame))
        {
            UpdateAllLayouts();
            InvalidateMeasure();
        }
    }

    // Selection highlight tracks the whole SelectedClips set (not just the primary
    // SelectedClip) so Ctrl-click / marquee multi-selection shows a border on every member.
    // Every selection change mutates SelectedClips (TimelineViewModel invariant), so this one
    // subscription covers single-select, Ctrl-click toggle, and clear alike.
    private void OnSelectedClipsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateAllSelections();
    }

    private void OnTransportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ComputeContentWidth multiplies TotalFrames by PixelsPerFrame; when a clip is
        // dropped (or removed) anywhere in the project the lane needs to re-measure so
        // the host ScrollViewer learns the new scrollable extent.
        if (e.PropertyName == nameof(TransportViewModel.TotalFrames))
        {
            InvalidateMeasure();
        }
    }

    private void OnClipsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Tracks hold a small number of clips — a full rebuild keeps the diff logic
        // trivial and isn't perf-relevant for this slice.
        RebuildClips();
    }

    private void OnClipPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Clip clip) return;
        if (!_clipViews.TryGetValue(clip, out var view)) return;

        // Fully qualified: `Clip` alone resolves to UIElement.Clip (a Geometry property)
        // inside this Canvas-derived type, so nameof on it fails to compile.
        if (e.PropertyName == nameof(Project.Clip.TimelineStart) || e.PropertyName == nameof(Project.Clip.Duration))
        {
            UpdateClipLayout(view, clip);
            InvalidateMeasure();
        }
    }

    private void RebuildClips()
    {
        foreach (var (clip, _) in _clipViews)
        {
            clip.PropertyChanged -= _clipHandler;
        }
        _clipViews.Clear();
        Children.Clear();

        if (Track is null) return;

        foreach (var clip in Track.Clips)
        {
            AddClipView(clip);
        }
        UpdateAllSelections();
        InvalidateMeasure();
    }

    private void AddClipView(Clip clip)
    {
        var view = new ClipView { DataContext = clip };
        _clipViews[clip] = view;
        clip.PropertyChanged += _clipHandler;
        Children.Add(view);
        UpdateClipLayout(view, clip);
    }

    private void UpdateClipLayout(ClipView view, Clip clip)
    {
        var px = Timeline?.PixelsPerFrame ?? 1.0;
        Canvas.SetLeft(view, clip.TimelineStart * px);
        Canvas.SetTop(view, ClipVerticalPadding);
        view.Width = Math.Max(MinClipWidth, clip.Duration * px);
        view.Height = TrackHeight - (ClipVerticalPadding * 2);
    }

    private void UpdateAllLayouts()
    {
        foreach (var (clip, view) in _clipViews)
        {
            UpdateClipLayout(view, clip);
        }
    }

    private void UpdateAllSelections()
    {
        // Clip is a plain reference type (no overridden Equals), so Contains is reference
        // identity — same semantics as the old ReferenceEquals(SelectedClip) check, extended
        // to the full multi-selection set.
        var selection = Timeline?.SelectedClips;
        foreach (var (clip, view) in _clipViews)
        {
            view.IsSelected = selection is not null && selection.Contains(clip);
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        // Canvas's default measure returns (0,0). Measure children so they layout, then
        // report the lane's natural content width so the host ScrollViewer can scroll.
        foreach (UIElement child in Children)
        {
            child.Measure(constraint);
        }

        var width = ComputeContentWidth();
        return new Size(width, TrackHeight);
    }

    private double ComputeContentWidth()
    {
        if (Timeline is null) return 0;
        var totalFrames = Math.Max(Timeline.Transport.TotalFrames, 1);
        return totalFrames * Timeline.PixelsPerFrame;
    }

    private void OnDragEnter(object sender, DragEventArgs e) => UpdateDragOperation(e);

    private void OnDragOver(object sender, DragEventArgs e) => UpdateDragOperation(e);

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        HideGhost();
        ClearRefusedTint();
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            var source = TryGetMediaSource(e);
            if (source is null || Track is null || Timeline is null) return;
            var streams = ResolveStreams(source, Track.Kind);
            if (streams is null) return;

            var framerate = Timeline.Project.Settings.Framerate;
            var draggedDuration = ComputeDraggedDuration(source, framerate);
            var landingFrame = LandingFrameFromCursor(e);
            var altHeld = (e.KeyStates & DragDropKeyStates.AltKey) == DragDropKeyStates.AltKey;
            var snapped = Timeline.Snap(landingFrame, Track, draggedDuration, altHeld);

            var clip = new MediaClip
            {
                MediaSourceId = source.Id,
                Source = source,
                SourceIn = TimeSpan.Zero,
                SourceOut = source.Duration,
                Streams = streams.Value,
                Framerate = framerate,
                TimelineStart = snapped,
            };

            // Insert in TimelineStart order so the underlying collection stays sorted —
            // makes downstream gap/edge logic (snap, overlap walk) deterministic.
            var insertIdx = Track.Clips.Count;
            for (var i = 0; i < Track.Clips.Count; i++)
            {
                if (Track.Clips[i].TimelineStart > snapped)
                {
                    insertIdx = i;
                    break;
                }
            }
            Track.Clips.Insert(insertIdx, clip);
            Timeline.SelectedClip = clip;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        finally
        {
            HideGhost();
            ClearRefusedTint();
        }
    }

    private void UpdateDragOperation(DragEventArgs e)
    {
        var source = TryGetMediaSource(e);
        if (source is null || Track is null || Timeline is null)
        {
            e.Effects = DragDropEffects.None;
            HideGhost();
            ClearRefusedTint();
            e.Handled = true;
            return;
        }

        var streams = ResolveStreams(source, Track.Kind);
        if (streams is null)
        {
            e.Effects = DragDropEffects.None;
            HideGhost();
            ApplyRefusedTint();
            e.Handled = true;
            return;
        }

        var framerate = Timeline.Project.Settings.Framerate;
        var draggedDuration = ComputeDraggedDuration(source, framerate);
        var landingFrame = LandingFrameFromCursor(e);
        var altHeld = (e.KeyStates & DragDropKeyStates.AltKey) == DragDropKeyStates.AltKey;
        var snapped = Timeline.Snap(landingFrame, Track, draggedDuration, altHeld);

        ClearRefusedTint();
        e.Effects = DragDropEffects.Copy;
        ShowGhost(snapped, draggedDuration, streams.Value);
        e.Handled = true;
    }

    private int LandingFrameFromCursor(DragEventArgs e)
    {
        if (Timeline is null || Timeline.PixelsPerFrame <= 0) return 0;
        var p = e.GetPosition(this);
        return (int)Math.Round(p.X / Timeline.PixelsPerFrame);
    }

    private static MediaSource? TryGetMediaSource(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(MediaSource)))
        {
            return e.Data.GetData(typeof(MediaSource)) as MediaSource;
        }
        return null;
    }

    //   HasVideo && HasAudio | Video → Both | Audio → Audio | Overlay → refused
    //   HasVideo only        | Video → Video                | Overlay → refused
    //   HasAudio only        | Audio → Audio                | Overlay → refused
    // Missing sources refuse on every track.
    private static ClipStreams? ResolveStreams(MediaSource source, TrackKind kind)
    {
        if (source.IsMissing) return null;
        return (kind, source.HasVideo, source.HasAudio) switch
        {
            (TrackKind.Video, true, true) => ClipStreams.Both,
            (TrackKind.Video, true, false) => ClipStreams.Video,
            (TrackKind.Audio, _, true) => ClipStreams.Audio,
            _ => null,
        };
    }

    private static int ComputeDraggedDuration(MediaSource source, int framerate)
    {
        if (framerate <= 0) return 0;
        return (int)Math.Round(source.Duration.TotalSeconds * framerate);
    }

    private void ApplyRefusedTint()
    {
        if (!ReferenceEquals(Background, RefusedLaneBackground))
        {
            Background = RefusedLaneBackground;
        }
    }

    private void ClearRefusedTint()
    {
        if (!ReferenceEquals(Background, DefaultLaneBackground))
        {
            Background = DefaultLaneBackground;
        }
    }

    private void ShowGhost(int frame, int durationFrames, ClipStreams streams)
    {
        if (Timeline is null) return;
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer is null) return;

        if (_ghostAdorner is null)
        {
            _ghostAdorner = new GhostClipAdorner(this);
            layer.Add(_ghostAdorner);
        }

        var ppf = Timeline.PixelsPerFrame;
        var x = frame * ppf;
        var width = Math.Max(MinClipWidth, durationFrames * ppf);
        var y = ClipVerticalPadding;
        var height = Math.Max(0, TrackHeight - (ClipVerticalPadding * 2));
        _ghostAdorner.UpdateRect(new Rect(x, y, width, height), streams);
    }

    private void HideGhost()
    {
        if (_ghostAdorner is null) return;
        var layer = AdornerLayer.GetAdornerLayer(this);
        layer?.Remove(_ghostAdorner);
        _ghostAdorner = null;
    }
}

/// <summary>
/// Translucent placeholder rectangle painted on the lane's <see cref="AdornerLayer"/>
/// during a media-bin drag. Lives on the lane (not the window) so its frame coordinates
/// share <see cref="ClipsLaneView"/>'s local space — the lane just tells it where the
/// snapped clip would land. Hit-test-transparent so it never intercepts the drag.
/// </summary>
internal sealed class GhostClipAdorner : Adorner
{
    private Rect _rect;
    private Brush _fill;

    public GhostClipAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
        _fill = BuildFill(ClipStreams.Both);
    }

    public void UpdateRect(Rect rect, ClipStreams streams)
    {
        _rect = rect;
        _fill = BuildFill(streams);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_rect.Width <= 0 || _rect.Height <= 0) return;
        dc.DrawRectangle(_fill, null, _rect);
    }

    // 40% opacity over the same colour family ClipView paints the final MediaClip in
    // (MediaClipFill #1F4F7A). The audio-track ghost gets a slightly cooler tint so the
    // split-stream case stays visually distinct from a Video-track drop.
    private static Brush BuildFill(ClipStreams streams)
    {
        var color = streams switch
        {
            ClipStreams.Audio => Color.FromArgb(0x66, 0x2C, 0x6A, 0x6A),
            _ => Color.FromArgb(0x66, 0x1F, 0x4F, 0x7A),
        };
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
