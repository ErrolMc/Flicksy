using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.Playback;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Root view-model for the video editor shell. Owns the document <see cref="Project"/>
/// plus the per-surface sub-VMs (<see cref="TitleBar"/>, <see cref="Preview"/>,
/// <see cref="Transport"/>, <see cref="Timeline"/>, <see cref="Inspector"/>,
/// <see cref="MediaBin"/>) and the shell UI state (selection, panel open/closed, rail tab). Owns the editor's <see cref="History"/>
/// (one <see cref="UndoManager"/> per editor window, mirroring PostSnip's
/// <c>DrawingViewModel.History</c>); the shell's <c>Ctrl+Z</c>/<c>Ctrl+Y</c> bindings invoke
/// <c>History.UndoCommand</c>/<c>RedoCommand</c>. Timeline-edit commands push onto it as #12's
/// gesture tools land.
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
        // History is a property initializer (runs before this body) so it's non-null here;
        // share the one UndoManager instance with the timeline and the title bar's Edit
        // menu so gesture tools and menu Undo/Redo push to / read from the same stack.
        Timeline = new TimelineViewModel(project, Transport, History);
        Inspector = new InspectorViewModel();
        MediaBin = new MediaBinViewModel(project);
        TitleBar = new TitleBarViewModel(History);

        // The engine drives the clock + audio output and writes Playhead/IsPlaying back onto
        // Transport (which Preview, Timeline and the ruler already observe). Attach after both
        // exist — the engine needs Transport, and Transport's commands delegate to the engine.
        // Preview is passed as the IPlaybackFrameSink so the engine can point it at the off-thread
        // decode-ahead pump during playback (ADR 0009).
        _playbackEngine = new PlaybackEngine(project, Transport, Preview);
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

    public TitleBarViewModel TitleBar { get; }

    public PreviewViewModel Preview { get; }

    public TransportViewModel Transport { get; }

    public TimelineViewModel Timeline { get; }

    public InspectorViewModel Inspector { get; }

    public MediaBinViewModel MediaBin { get; }

    public IReadOnlyList<RailItem> LeftRailItems { get; }

    public IReadOnlyList<RailItem> RightRailItems { get; }

    /// <summary>
    /// The editor's undo stack. Timeline-edit gestures (#12) push before/after-snapshot
    /// commands here; Ctrl+Z/Ctrl+Y bind to its <see cref="UndoManager.UndoCommand"/> /
    /// <see cref="UndoManager.RedoCommand"/>.
    /// </summary>
    public UndoManager History { get; } = new();

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

    public void Dispose()
    {
        if (_disposed) 
            return;

        _disposed = true;
        // Engine first: stops the clock + audio output and unhooks Rendering before the
        // compositor's decoder cache (which the preview's last render may still touch) goes.
        _playbackEngine.Dispose();
        // Preview next: joins its background scrub worker before the shared compositor (which the
        // worker's present path uses) is torn down.
        Preview.Dispose();
        _compositor.Dispose();
    }
}
