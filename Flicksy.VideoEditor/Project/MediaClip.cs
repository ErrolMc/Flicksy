using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Flicksy.VideoEditor.Project;

/// <summary>
/// A clip that plays a region of a <see cref="MediaSource"/> on the timeline. The clip
/// references its source by <see cref="MediaSourceId"/>; the file path lives on the
/// <see cref="MediaSource"/> only (see ADR 0003).
/// <para>
/// <see cref="SourceIn"/>/<see cref="SourceOut"/> are framerate-independent
/// <see cref="System.TimeSpan"/> values into the source; <see cref="Duration"/> is the
/// resulting span on the timeline in integer frames after applying <see cref="Speed"/> at
/// the project's framerate. <see cref="Framerate"/> is a local mirror of
/// <see cref="ProjectSettings.Framerate"/> kept in sync by the parent <see cref="Project"/>
/// so <see cref="Duration"/> can be a parameterless getter without a back-reference.
/// </para>
/// <para>
/// <see cref="Source"/> is a transient convenience reference, populated by
/// <see cref="Project"/> factories (and, in the future, the load resolver). It is
/// <see cref="JsonIgnoreAttribute">not serialized</see>. <strong>All mutations
/// (relocate, remove, etc.) must drive off <see cref="MediaSourceId"/> lookups in
/// <see cref="Project.MediaSources"/></strong> — never treat <see cref="Source"/> as
/// source of truth.
/// </para>
/// </summary>
public partial class MediaClip : Clip
{
    public Guid MediaSourceId { get; init; }

    [ObservableProperty]
    private TimeSpan sourceIn;

    [ObservableProperty]
    private TimeSpan sourceOut;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Duration))]
    private double speed = 1.0;

    [ObservableProperty]
    private double volume = 1.0;

    [ObservableProperty]
    private ClipStreams streams = ClipStreams.Both;

    // Per-clip name override. Empty by default — DisplayName falls through to the
    // source's display name so bin renames flow to the clip label. Set by user rename
    // through the ClipView context menu. (The Detach audio command leaves this empty and
    // lets DisplayName auto-derive "<source> (Audio)" so the label still tracks bin
    // renames of the source.)
    [ObservableProperty]
    private string name = string.Empty;

    // Inline-rename buffer. Flipped to true by BeginRename; the ClipView's MediaClip
    // template swaps the name TextBlock for a TextBox bound to EditingName. Transient —
    // not serialized — the persisted state is just Name.
    [JsonIgnore]
    [ObservableProperty]
    private bool isEditing;

    [JsonIgnore]
    [ObservableProperty]
    private string editingName = string.Empty;

    // Mirrors the parent Project's framerate. Project is responsible for keeping
    // this in sync; storing it on the clip lets Duration be a parameterless getter
    // without a back-reference to Project.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Duration))]
    private int framerate = 30;

    // Transient convenience ref — populated by Project factories. Not the source of truth:
    // mutations always look up by MediaSourceId in Project.MediaSources.
    [JsonIgnore]
    [ObservableProperty]
    private MediaSource? source;

    public Transform2D Transform { get; } = new();

    public ObservableCollection<Filter> Filters { get; } = new();

    // Subscribed source — kept in sync with Source via OnSourceChanged. Listening lets
    // IsBroken react when an external mutation flips IsMissing or rewrites the stream
    // shape (e.g. Relocate replacing the file behind a MediaSource instance).
    private MediaSource? _subscribedSource;

    partial void OnSourceInChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(IsBroken));
    }

    partial void OnSourceOutChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(IsBroken));
    }

    partial void OnStreamsChanged(ClipStreams value)
    {
        OnPropertyChanged(nameof(IsBroken));
        // Streams flips the auto-derived branch (Audio over a HasVideo source gets the
        // "(Audio)" suffix); recompute the label when it changes.
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnSourceChanged(MediaSource? value)
    {
        if (_subscribedSource is not null) _subscribedSource.PropertyChanged -= OnSourcePropertyChanged;
        _subscribedSource = value;
        if (_subscribedSource is not null) _subscribedSource.PropertyChanged += OnSourcePropertyChanged;
        OnPropertyChanged(nameof(IsBroken));
        OnPropertyChanged(nameof(DisplayName));
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MediaSource.IsMissing):
            case nameof(MediaSource.Duration):
                OnPropertyChanged(nameof(IsBroken));
                break;
            case nameof(MediaSource.HasVideo):
            case nameof(MediaSource.HasAudio):
                // Affects IsBroken (stream mismatch) and DisplayName (the auto-derived
                // "(Audio)" suffix depends on HasVideo).
                OnPropertyChanged(nameof(IsBroken));
                OnPropertyChanged(nameof(DisplayName));
                break;
            case nameof(MediaSource.DisplayName):
                // Only relevant when Name is unset — otherwise DisplayName ignores the
                // source. Cheap to over-notify, keep it unconditional.
                OnPropertyChanged(nameof(DisplayName));
                break;
        }
    }

    public override int Duration
    {
        get
        {
            var sourceSeconds = (SourceOut - SourceIn).TotalSeconds;
            if (Speed <= 0 || sourceSeconds <= 0 || Framerate <= 0)
            {
                return 0;
            }

            var timelineSeconds = sourceSeconds / Speed;
            return (int)Math.Round(timelineSeconds * Framerate);
        }
    }

    /// <summary>
    /// Label rendered on the clip in the timeline. Three-state precedence:
    /// (1) if the user has set <see cref="Name"/> (non-empty), use it verbatim — frozen
    /// against later bin renames; (2) otherwise auto-derive from the source — appending
    /// " (Audio)" when this is the audio half of a split (<see cref="Streams"/> =
    /// <see cref="ClipStreams.Audio"/> over a <see cref="MediaSource.HasVideo"/> source);
    /// (3) otherwise return <see cref="MediaSource.DisplayName"/> directly. (1) freezes,
    /// (2) and (3) track bin renames live.
    /// </summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(Name)) return Name;
            return AutoDerivedDisplayName;
        }
    }

    // The label this clip would show if the user hadn't renamed it. CommitRename also
    // uses it to detect "user typed back the auto-derived value" and clear the override.
    private string AutoDerivedDisplayName
    {
        get
        {
            var sourceName = Source?.DisplayName ?? string.Empty;
            return Streams == ClipStreams.Audio && Source is { HasVideo: true }
                ? $"{sourceName} (Audio)"
                : sourceName;
        }
    }

    /// <summary>
    /// Opens the inline-rename TextBox on this clip with the current <see cref="DisplayName"/>
    /// as the seed. Idempotent — calling it on an already-editing clip just refreshes the
    /// seed.
    /// </summary>
    public void BeginRename()
    {
        EditingName = DisplayName;
        IsEditing = true;
    }

    /// <summary>
    /// Closes the rename editor and writes the buffer to <see cref="Name"/>. Empty /
    /// whitespace input, or input that matches the auto-derived label, clears the
    /// override — <see cref="DisplayName"/> then falls back to the source so future bin
    /// renames flow through again (including the "(Audio)" suffix for split-off audio
    /// clips). The early-return on <c>!IsEditing</c> guards against the
    /// LostFocus-after-Enter double-fire (Enter commits, TextBox collapses, the collapse
    /// fires LostFocus which re-enters here).
    /// </summary>
    public void CommitRename()
    {
        if (!IsEditing) return;

        var trimmed = (EditingName ?? string.Empty).Trim();
        Name = string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, AutoDerivedDisplayName, StringComparison.Ordinal)
            ? string.Empty
            : trimmed;

        IsEditing = false;
        EditingName = string.Empty;
    }

    /// <summary>
    /// Closes the rename editor without writing the buffer.
    /// </summary>
    public void CancelRename()
    {
        IsEditing = false;
        EditingName = string.Empty;
    }

    /// <summary>
    /// True when the clip can't render correctly: source is missing, the referenced range
    /// extends past the source's duration, or the requested <see cref="Streams"/> requires
    /// a stream the source doesn't have. The timeline visual uses this to red-out the clip
    /// without mutating any clip data — the user fixes it by Relocate-ing the source or
    /// trimming the clip.
    /// </summary>
    [JsonIgnore]
    public bool IsBroken
    {
        get
        {
            if (Source is null || Source.IsMissing) return true;
            if (SourceOut > Source.Duration) return true;
            return (Streams, Source.HasVideo, Source.HasAudio) switch
            {
                (ClipStreams.Both, true, true) => false,
                (ClipStreams.Video, true, _) => false,
                (ClipStreams.Audio, _, true) => false,
                _ => true,
            };
        }
    }
}
