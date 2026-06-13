using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.Playback;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Root view-model for the video editor shell. Owns the document <see cref="Project"/> plus the
/// per-surface sub-VMs (<see cref="TitleBar"/>, <see cref="Preview"/>, <see cref="Transport"/>,
/// <see cref="Timeline"/>, <see cref="Inspector"/>, <see cref="MediaBin"/>) and the shell UI state
/// (selection, panel open/closed, rail tab). The cross-cutting collaborators it threads into those
/// sub-VMs — the shared <see cref="History"/> (<see cref="IUndoService"/>), the
/// <see cref="OverlayHost"/> (<see cref="IOverlayService"/>), the <see cref="ICompositor"/>,
/// <see cref="IProjectSettingsService"/> and <see cref="ISettingsService"/> — are injected from the container; the per-document
/// sub-VMs are composed here around the runtime <see cref="Project"/> (which the container does
/// not hold). One document per process today — see the scope note in
/// <see cref="Services.ServiceCollectionExtensions"/> for the tabs/MDI evolution.
/// </summary>
public partial class VideoEditorViewModel : ObservableObject, IDisposable
{
    private readonly ICompositor _compositor;
    private readonly bool _ownsCompositor;
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

    /// <summary>
    /// DI constructor. The runtime <see cref="Project"/> is supplied by <see cref="IEditorFactory"/>
    /// (positionally, via <see cref="ActivatorUtilities"/>); the cross-cutting collaborators come
    /// from the container. The per-document sub-VMs are composed here so they all share the one
    /// runtime project plus the injected <paramref name="history"/>/<paramref name="overlayHost"/>/
    /// <paramref name="compositor"/>.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public VideoEditorViewModel(
        Project.Project project,
        UndoManager history,
        OverlayHostViewModel overlayHost,
        ICompositor compositor,
        IProjectSettingsService projectSettings,
        ISettingsService settings)
    {
        Project = project;
        History = history;
        OverlayHost = overlayHost;
        // One compositor per editor window (decoder cache + Skia state). On the DI path the
        // container owns it; the convenience ctor below flags _ownsCompositor so it disposes
        // the one it created instead.
        _compositor = compositor;

        Transport = new TransportViewModel(project);
        Preview = new PreviewViewModel(project, Transport, compositor);
        // Share the one UndoManager with the timeline and the title bar's Edit menu so gesture
        // tools and menu Undo/Redo push to / read from the same stack.
        Timeline = new TimelineViewModel(project, Transport, history);
        Inspector = new InspectorViewModel();
        MediaBin = new MediaBinViewModel(project);
        // TitleBar opens overlays through the shared overlay host (as IOverlayService) and reads
        // the document's settings through IProjectSettingsService plus editor-wide settings through
        // ISettingsService — so it never needs a root-VM reference and its menu commands stay fully
        // DI-constructible.
        TitleBar = new TitleBarViewModel(history, overlayHost, projectSettings, settings);

        // The engine drives the clock + audio output and writes Playhead/IsPlaying back onto
        // Transport (which Preview, Timeline and the ruler already observe). Attach after both
        // exist — the engine needs Transport, and Transport's commands delegate to the engine.
        // Preview is passed as the IPlaybackFrameSink so the engine can point it at the off-thread
        // decode-ahead pump during playback (ADR 0009).
        _playbackEngine = new PlaybackEngine(project, Transport, Preview);
        Transport.AttachPlaybackController(_playbackEngine);

        // Timeline.SelectedClip is the user-facing write side (clip clicks); root's
        // SelectedClip is what every other surface reads (right rail, inspectors). Sync both
        // ways so a click in the timeline flows up and an external clear flows back down.
        Timeline.PropertyChanged += OnTimelinePropertyChanged;
    }

    /// <summary>
    /// Convenience constructor for design-time and the window's fallback ctors: builds the
    /// cross-cutting collaborators by hand (mirroring the DI registrations) and delegates to the
    /// DI constructor. The compositor created here is owned by this VM and disposed in
    /// <see cref="Dispose"/>; on the DI path the container owns it instead.
    /// </summary>
    public VideoEditorViewModel(Project.Project project)
        : this(
            project,
            new UndoManager(),
            new OverlayHostViewModel(),
            new SkiaCompositor(),
            new ProjectSettingsService { Current = project.Settings },
            new SettingsService())
    {
        _ownsCompositor = true;
    }

    public Project.Project Project { get; }

    public TitleBarViewModel TitleBar { get; }

    public PreviewViewModel Preview { get; }

    public TransportViewModel Transport { get; }

    public TimelineViewModel Timeline { get; }

    public InspectorViewModel Inspector { get; }

    public MediaBinViewModel MediaBin { get; }

    public IReadOnlyList<RailItem> LeftRailItems { get; } = new[]
    {
        new RailItem { Label = "Media", Glyph = "M", Tag = LeftRailTab.Media },
        new RailItem { Label = "Text", Glyph = "T", Tag = LeftRailTab.Text },
        new RailItem { Label = "Shapes", Glyph = "S", Tag = LeftRailTab.Shapes },
        new RailItem { Label = "Pen", Glyph = "P", Tag = LeftRailTab.Pen },
        new RailItem { Label = "Transitions", Glyph = "Tr", Tag = LeftRailTab.Transitions },
    };

    public IReadOnlyList<RailItem> RightRailItems { get; } = new[]
    {
        new RailItem { Label = "Speed", Glyph = "Sp", Tag = RightRailTab.Speed },
        new RailItem { Label = "Audio", Glyph = "Au", Tag = RightRailTab.Audio },
        new RailItem { Label = "Adjust colors", Glyph = "Co", Tag = RightRailTab.AdjustColors },
        new RailItem { Label = "Filters", Glyph = "Fi", Tag = RightRailTab.Filters },
        new RailItem { Label = "Fade", Glyph = "Fa", Tag = RightRailTab.Fade },
    };

    /// <summary>
    /// The editor's undo stack (injected; one <see cref="UndoManager"/> per editor window).
    /// Timeline-edit gestures (#12) push before/after-snapshot commands here; Ctrl+Z/Ctrl+Y
    /// bind to its <see cref="UndoManager.UndoCommand"/> / <see cref="UndoManager.RedoCommand"/>.
    /// </summary>
    public UndoManager History { get; }

    /// <summary>
    /// The shell's modal overlay layer (Project Settings / Settings / future Export), injected as
    /// the shared <see cref="IOverlayService"/> implementation. <see cref="Controls.OverlayHost"/>
    /// binds this; the window's key handling light-dismisses on Esc and gates editor shortcuts
    /// while an overlay is open.
    /// </summary>
    public OverlayHostViewModel OverlayHost { get; }

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
        // Dispose the compositor only when this VM created it (design-time/fallback path); on the
        // DI path the container owns the singleton and disposes it at host shutdown.
        if (_ownsCompositor)
        {
            _compositor.Dispose();
        }
    }
}
