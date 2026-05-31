using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.Playback;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Root view-model for the video editor shell. Owns the document <see cref="Project"/>
/// plus the per-surface sub-VMs (<see cref="Preview"/>, <see cref="Transport"/>,
/// <see cref="Timeline"/>, <see cref="Inspector"/>, <see cref="MediaBin"/>) and the shell
/// UI state (selection, panel open/closed, rail tab). Undo/redo commands are no-ops in
/// this slice; they exist so the shell's <c>Ctrl+Z</c>/<c>Ctrl+Y</c> input bindings have
/// something to invoke until the timeline-edit undo stack lands in #12.
/// </summary>
public partial class VideoEditorViewModel : ObservableObject, IDisposable
{
    private readonly ICompositor _compositor;
    private readonly PlaybackEngine _playbackEngine;
    private bool _disposed;

    [ObservableProperty]
    private string projectName = "Untitled Project";

    [ObservableProperty]
    private Clip? selectedClip;

    [ObservableProperty]
    private LeftRailTab currentLeftTab = LeftRailTab.Media;

    [ObservableProperty]
    private RightRailTab currentRightTab = RightRailTab.Speed;

    [ObservableProperty]
    private bool isLeftPanelOpen = true;

    // Right panel starts closed — its tabs are clip-scoped and there's no selection yet.
    [ObservableProperty]
    private bool isRightPanelOpen;

    public VideoEditorViewModel(Project.Project project)
    {
        Project = project;
        // One compositor per editor window. Decoder cache + Skia state live for the
        // lifetime of the project; Dispose tears them down when the window closes.
        _compositor = new SkiaCompositor();
        Transport = new TransportViewModel(project);
        Preview = new PreviewViewModel(project, Transport, _compositor);
        Timeline = new TimelineViewModel(project, Transport);
        Inspector = new InspectorViewModel();
        MediaBin = new MediaBinViewModel(project);

        // The engine drives the clock + audio output and writes Playhead/IsPlaying back onto
        // Transport (which Preview, Timeline and the ruler already observe). Attach after both
        // exist — the engine needs Transport, and Transport's commands delegate to the engine.
        _playbackEngine = new PlaybackEngine(project, Transport);
        Transport.AttachPlaybackController(_playbackEngine);

        // Timeline.SelectedClip is the user-facing write side (clip clicks); root's
        // SelectedClip is what every other surface reads (right rail, inspectors).
        // Sync both ways so a click in the timeline flows up and an external clear
        // (or future programmatic select) flows back down.
        Timeline.PropertyChanged += OnTimelinePropertyChanged;

        LeftRailItems = new[]
        {
            new RailItem { Label = "Media", Glyph = "M", Tag = LeftRailTab.Media },
            new RailItem { Label = "Text", Glyph = "T", Tag = LeftRailTab.Text },
            new RailItem { Label = "Shapes", Glyph = "S", Tag = LeftRailTab.Shapes },
            new RailItem { Label = "Pen", Glyph = "P", Tag = LeftRailTab.Pen },
            new RailItem { Label = "Transitions", Glyph = "Tr", Tag = LeftRailTab.Transitions },
        };

        RightRailItems = new[]
        {
            new RailItem { Label = "Speed", Glyph = "Sp", Tag = RightRailTab.Speed },
            new RailItem { Label = "Audio", Glyph = "Au", Tag = RightRailTab.Audio },
            new RailItem { Label = "Adjust colors", Glyph = "Co", Tag = RightRailTab.AdjustColors },
            new RailItem { Label = "Filters", Glyph = "Fi", Tag = RightRailTab.Filters },
            new RailItem { Label = "Fade", Glyph = "Fa", Tag = RightRailTab.Fade },
        };
    }

    public Project.Project Project { get; }

    public PreviewViewModel Preview { get; }

    public TransportViewModel Transport { get; }

    public TimelineViewModel Timeline { get; }

    public InspectorViewModel Inspector { get; }

    public MediaBinViewModel MediaBin { get; }

    public IReadOnlyList<RailItem> LeftRailItems { get; }

    public IReadOnlyList<RailItem> RightRailItems { get; }

    partial void OnSelectedClipChanged(Clip? value)
    {
        if (Timeline.SelectedClip != value)
        {
            Timeline.SelectedClip = value;
        }
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.SelectedClip) && SelectedClip != Timeline.SelectedClip)
        {
            SelectedClip = Timeline.SelectedClip;
        }
    }

    [RelayCommand]
    private void Undo()
    {
        // No-op in this slice. Real undo stack lands in #12 (timeline editing).
    }

    [RelayCommand]
    private void Redo()
    {
    }

    [RelayCommand]
    private void Export()
    {
        // No-op in this slice. Real exporter lands in #20.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Engine first: stops the clock + audio output and unhooks Rendering before the
        // compositor's decoder cache (which the preview's last render may still touch) goes.
        _playbackEngine.Dispose();
        _compositor.Dispose();
    }
}
