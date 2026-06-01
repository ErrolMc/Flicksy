using System;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.VideoEditor.Playback;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// State and commands for the Transport bar: the integer-frame <see cref="Playhead"/>,
/// the <see cref="IsPlaying"/> flag, and the play/pause + frame-step commands the
/// TransportView binds to. Timecode strings are computed off <see cref="Playhead"/> /
/// <see cref="TotalFrames"/> at the project framerate. <see cref="TotalFrames"/> tracks
/// the project's clip set live (re-derived when tracks or clips mutate) so the ruler,
/// lane content width, and seek clamp all stay in step with bin-to-timeline drops and
/// Detach-audio track additions.
/// <para>
/// Play/pause and frame-step delegate to the attached <see cref="IPlaybackController"/>
/// (the <see cref="Playback.PlaybackEngine"/>), which owns the clock and writes
/// <see cref="Playhead"/> / <see cref="IsPlaying"/> back here. When no controller is attached
/// (e.g. unit tests) the commands fall back to manipulating the playhead directly.
/// </para>
/// </summary>
public partial class TransportViewModel : ObservableObject
{
    private readonly Project.Project _project;
    private readonly int _framerate;
    private IPlaybackController? _playback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTimecode))]
    private int playhead;

    // Toggled by the play/pause button. No real playback yet — the flag exists so the
    // button can flip its label and future bindings have something to observe.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseLabel))]
    private bool isPlaying;

    // Live-computed from the project's clip set. The view layer (TimeRulerView,
    // ClipsLaneView, PlayheadView) listens for this property change and re-measures so
    // the timeline grows when a clip is dropped and shrinks when one is removed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalTimecode))]
    private int totalFrames;

    public TransportViewModel(Project.Project project)
    {
        _project = project;
        _framerate = project.Settings.Framerate;

        SubscribeProjectMutations();
        TotalFrames = ComputeTotalFrames(project);
    }

    public string TotalTimecode => FormatTimecode(TotalFrames, _framerate);

    public string CurrentTimecode => FormatTimecode(Playhead, _framerate);

    public string PlayPauseLabel => IsPlaying ? "Pause" : "Play";

    /// <summary>
    /// Wire the transport's commands to the playback engine. Called once by
    /// <see cref="VideoEditorViewModel"/> after both are constructed (the engine needs this VM,
    /// and this VM delegates back to the engine — a two-step attach breaks the cycle).
    /// </summary>
    public void AttachPlaybackController(IPlaybackController controller) => _playback = controller;

    [RelayCommand]
    private void PlayPause()
    {
        if (_playback is not null)
        {
            _playback.TogglePlayPause();
            return;
        }
        IsPlaying = !IsPlaying;
    }

    [RelayCommand]
    private void PrevFrame()
    {
        if (_playback is not null)
        {
            _playback.StepFrame(-1);
            return;
        }
        Playhead = Math.Max(0, Playhead - 1);
    }

    [RelayCommand]
    private void NextFrame()
    {
        if (_playback is not null)
        {
            _playback.StepFrame(1);
            return;
        }
        Playhead = TotalFrames > 0
            ? Math.Min(TotalFrames, Playhead + 1)
            : Playhead + 1;
    }

    private void SubscribeProjectMutations()
    {
        _project.Tracks.CollectionChanged += OnTracksCollectionChanged;
        foreach (var track in _project.Tracks)
        {
            SubscribeTrack(track);
        }
    }

    private void SubscribeTrack(Track track)
    {
        track.Clips.CollectionChanged += OnTrackClipsChanged;
        foreach (var clip in track.Clips)
        {
            clip.PropertyChanged += OnClipPropertyChanged;
        }
    }

    private void UnsubscribeTrack(Track track)
    {
        track.Clips.CollectionChanged -= OnTrackClipsChanged;
        foreach (var clip in track.Clips)
        {
            clip.PropertyChanged -= OnClipPropertyChanged;
        }
    }

    private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (Track t in e.OldItems) UnsubscribeTrack(t);
        }
        if (e.NewItems is not null)
        {
            foreach (Track t in e.NewItems) SubscribeTrack(t);
        }
        TotalFrames = ComputeTotalFrames(_project);
    }

    private void OnTrackClipsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (Clip c in e.OldItems) c.PropertyChanged -= OnClipPropertyChanged;
        }
        if (e.NewItems is not null)
        {
            foreach (Clip c in e.NewItems) c.PropertyChanged += OnClipPropertyChanged;
        }
        TotalFrames = ComputeTotalFrames(_project);
    }

    private void OnClipPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Clip.TimelineStart) || e.PropertyName == nameof(Clip.Duration))
        {
            TotalFrames = ComputeTotalFrames(_project);
        }
    }

    private static int ComputeTotalFrames(Project.Project project)
    {
        int max = 0;
        foreach (var track in project.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                var end = clip.TimelineStart + clip.Duration;
                if (end > max) max = end;
            }
        }
        return max;
    }

    // hh:mm:ss.ff where ff is the frame index inside the current second.
    private static string FormatTimecode(int frames, int framerate)
    {
        if (framerate <= 0) framerate = 1;
        var totalSeconds = frames / framerate;
        var frame = frames % framerate;
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds / 60) % 60;
        var seconds = totalSeconds % 60;
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}.{frame:D2}";
    }
}
