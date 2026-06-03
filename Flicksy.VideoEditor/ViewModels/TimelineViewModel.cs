using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// State for the timeline surface: the document <see cref="Project"/> whose tracks/clips
/// it renders, the <see cref="Transport"/> whose <c>Playhead</c> drives the overlay, the
/// <see cref="PixelsPerFrame"/> zoom level, and the currently <see cref="SelectedClip"/>.
/// Selection is mirrored by <see cref="VideoEditorViewModel"/> so the right rail stays in
/// sync — the timeline writes the user's click here and the root forwards to its own
/// SelectedClip (and vice versa when selection is cleared elsewhere).
/// <para>
/// <see cref="SelectedClips"/> is the full multi-selection set (#12); <see cref="SelectedClip"/>
/// is the <em>primary</em> — null iff the set is empty, and always a member of the set when
/// non-null. The right rail / inspector continue to read the single primary, so their wiring
/// is unchanged while multi-clip gestures (delete / split / move) operate on the set. The two
/// are kept consistent by <see cref="SetSelection"/> / <see cref="OnSelectedClipChanged"/>.
/// </para>
/// </summary>
public partial class TimelineViewModel : ObservableObject
{
    public const double MinPixelsPerFrame = 0.025;
    public const double MaxPixelsPerFrame = 60.0;

    // Snap pull radius in screen pixels, applied to the dragged clip's start edge against
    // every clip edge on the target track plus the playhead. Tightens at high zoom and
    // loosens at low zoom because it's converted to frames via PixelsPerFrame at call time.
    private const double SnapRadiusPixels = 6.0;

    // Guards the SelectedClip <-> SelectedClips sync so neither side's write re-triggers the
    // other. SetSelection drives the set and lets OnSelectedClipChanged skip its own rebuild;
    // a bare SelectedClip write (today's single-select click) rebuilds the set to match.
    private bool _syncingSelection;

    [ObservableProperty]
    private double pixelsPerFrame = 6.0;

    [ObservableProperty]
    private Clip? selectedClip;

    public TimelineViewModel(Project.Project project, TransportViewModel transport, UndoManager history)
    {
        Project = project;
        Transport = transport;
        History = history;
    }

    public Project.Project Project { get; }

    public TransportViewModel Transport { get; }

    /// <summary>
    /// The editor's undo stack, shared with <see cref="VideoEditorViewModel.History"/> (same
    /// instance) so the toolbar buttons / Ctrl+Z / Ctrl+Y and the timeline gesture tools push
    /// to one stack. Move / trim / split / delete commands (#12) are pushed here on gesture end.
    /// </summary>
    public UndoManager History { get; }

    /// <summary>
    /// The full multi-selection set. Mutated through <see cref="SetSelection"/> (and, for a
    /// single-select click, kept in sync from <see cref="OnSelectedClipChanged"/>) so the
    /// invariant holds: <see cref="SelectedClip"/> is null iff this set is empty, and is always
    /// a member of the set otherwise. Multi-clip gestures (delete / split / move) read this;
    /// the right rail / inspector read the single <see cref="SelectedClip"/> primary.
    /// </summary>
    public ObservableCollection<Clip> SelectedClips { get; } = new();

    /// <summary>
    /// Replaces the selection with <paramref name="clips"/>, choosing <paramref name="primary"/>
    /// (or the first clip when null/absent) as <see cref="SelectedClip"/>. Empty input clears
    /// the selection. This is the entry point future multi-select gestures (marquee, Ctrl-click)
    /// use; single-click selection still flows through a bare <see cref="SelectedClip"/> write.
    /// </summary>
    public void SetSelection(IEnumerable<Clip> clips, Clip? primary = null)
    {
        var distinct = clips?.Distinct().ToList() ?? new List<Clip>();

        _syncingSelection = true;
        try
        {
            SelectedClips.Clear();
            foreach (var clip in distinct)
            {
                SelectedClips.Add(clip);
            }

            SelectedClip = distinct.Count == 0
                ? null
                : (primary is not null && distinct.Contains(primary) ? primary : distinct[0]);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    // A bare SelectedClip write (single-select click, or an external clear from the root VM)
    // rebuilds the set to match: {clip} when non-null, empty when null. Skipped while
    // SetSelection is mid-update so a multi-select isn't collapsed to its primary.
    partial void OnSelectedClipChanged(Clip? value)
    {
        if (_syncingSelection) return;

        SelectedClips.Clear();
        if (value is not null)
        {
            SelectedClips.Add(value);
        }
    }

    /// <summary>
    /// Toggles <paramref name="clip"/> in the selection (Ctrl-click semantics). Adding makes
    /// it the new primary; removing promotes another remaining clip (or clears to empty). The
    /// "primary is null iff set is empty, else a member" invariant is preserved throughout.
    /// </summary>
    public void ToggleSelection(Clip clip)
    {
        if (clip is null) return;

        _syncingSelection = true;
        try
        {
            if (SelectedClips.Contains(clip))
            {
                SelectedClips.Remove(clip);
                if (ReferenceEquals(SelectedClip, clip))
                {
                    SelectedClip = SelectedClips.Count > 0 ? SelectedClips[0] : null;
                }
            }
            else
            {
                SelectedClips.Add(clip);
                SelectedClip = clip;
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// Multiplies <see cref="PixelsPerFrame"/> by <paramref name="factor"/>, clamped to the
    /// supported range. The caller (timeline view's wheel handler) is responsible for
    /// restoring the scroll offset so the zoom appears centered on the playhead.
    /// </summary>
    public void ZoomBy(double factor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor)) return;
        PixelsPerFrame = Math.Clamp(PixelsPerFrame * factor, MinPixelsPerFrame, MaxPixelsPerFrame);
    }

    /// <summary>
    /// Sets <see cref="TransportViewModel.Playhead"/> to <paramref name="frame"/>, clamped
    /// to <c>[0, TotalFrames]</c>. Used by scrub gestures on the ruler and the empty lane
    /// area; clip-internal scrubbing (drag the playhead handle itself) is a later slice.
    /// </summary>
    public void SeekToFrame(int frame)
    {
        var max = Math.Max(0, Transport.TotalFrames);
        Transport.Playhead = Math.Clamp(frame, 0, max);
    }

    /// <summary>
    /// Convenience: convert a lane-relative pixel offset to a frame and seek there.
    /// </summary>
    public void SeekToPixel(double laneX)
    {
        if (PixelsPerFrame <= 0) return;
        SeekToFrame((int)Math.Round(laneX / PixelsPerFrame));
    }

    /// <summary>
    /// Resolves a desired landing frame for a clip of <paramref name="draggedDuration"/>
    /// frames being placed on <paramref name="targetTrack"/>. Two-stage:
    /// (1) When <paramref name="altHeld"/> is false, snap the start edge to the nearest
    /// candidate within <see cref="SnapRadiusPixels"/> — every clip's start + end <em>across
    /// all tracks</em> (cross-track alignment), plus <see cref="TransportViewModel.Playhead"/>
    /// and frame 0. Alt bypasses this stage.
    /// (2) Enforce the non-destructive overlap rule: if the resulting [start, start+duration)
    /// rect intersects any existing clip on the track, walk the start to the closest free
    /// gap that fits. Existing clips are never shifted. This stage runs regardless of Alt —
    /// the timeline always has non-overlapping clips per track.
    /// <paramref name="excludeClips"/> are omitted from both stages — pass the clip(s) being
    /// moved so they neither snap to their own edges nor block their own gap. Used by
    /// bin-to-timeline drops (no exclusion) and single-clip move (exclude the dragged clip).
    /// </summary>
    public int Snap(int landingFrame, Track targetTrack, int draggedDuration, bool altHeld, IReadOnlyCollection<Clip>? excludeClips = null)
    {
        var frame = Math.Max(0, landingFrame);

        if (!altHeld && PixelsPerFrame > 0)
        {
            frame = ApplyEdgeSnap(frame, excludeClips);
        }

        frame = WalkToFreeGap(frame, targetTrack, Math.Max(0, draggedDuration), excludeClips);
        return Math.Max(0, frame);
    }

    /// <summary>
    /// Snaps a desired start frame to the nearest edge candidate (clip edges across all tracks,
    /// playhead, frame 0) within <see cref="SnapRadiusPixels"/>, excluding <paramref name="excludeClips"/>.
    /// The gap-walk stage of <see cref="Snap"/> is skipped — this is the edge-snap-only entry the
    /// rigid multi-move group uses to snap its anchor's start without per-clip gap relocation.
    /// </summary>
    public int SnapStartEdge(int desiredStart, IReadOnlyCollection<Clip>? excludeClips = null)
    {
        var frame = Math.Max(0, desiredStart);
        if (PixelsPerFrame > 0)
        {
            frame = ApplyEdgeSnap(frame, excludeClips);
        }
        return Math.Max(0, frame);
    }

    // Edge-snap candidates span every track (so clips align across lanes), plus the playhead
    // and frame 0. excludeClips drops the moving clip(s)' own edges so a drag doesn't stick to
    // where the clip already is.
    private int ApplyEdgeSnap(int frame, IReadOnlyCollection<Clip>? excludeClips)
    {
        var snapRadiusFrames = SnapRadiusPixels / PixelsPerFrame;
        var best = frame;
        var bestDelta = snapRadiusFrames;

        void Consider(int candidate)
        {
            var delta = Math.Abs(candidate - frame);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = candidate;
            }
        }

        foreach (var track in Project.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (excludeClips is not null && excludeClips.Contains(clip)) continue;
                Consider(clip.TimelineStart);
                Consider(clip.TimelineStart + clip.Duration);
            }
        }
        Consider(Transport.Playhead);
        Consider(0);

        return best;
    }

    // Build the sorted list of occupied [start, end) intervals on the track (skipping
    // excludeClips), then either accept the desired placement (if it fits) or pick the
    // gap-clamped placement closest to it. The tail gap is unbounded, so a valid placement
    // always exists.
    private static int WalkToFreeGap(int desiredStart, Track targetTrack, int draggedDuration, IReadOnlyCollection<Clip>? excludeClips = null)
    {
        if (draggedDuration <= 0)
        {
            return Math.Max(0, desiredStart);
        }

        var occupied = targetTrack.Clips
            .Where(c => excludeClips is null || !excludeClips.Contains(c))
            .Select(c => (Start: c.TimelineStart, End: c.TimelineStart + Math.Max(1, c.Duration)))
            .OrderBy(i => i.Start)
            .ToList();

        if (occupied.Count == 0)
        {
            return Math.Max(0, desiredStart);
        }

        var desiredEnd = desiredStart + draggedDuration;
        var overlaps = occupied.Any(i => desiredStart < i.End && i.Start < desiredEnd);
        if (!overlaps)
        {
            return Math.Max(0, desiredStart);
        }

        var candidates = new List<int>();

        var leadEnd = occupied[0].Start;
        if (leadEnd >= draggedDuration)
        {
            var maxPlacement = leadEnd - draggedDuration;
            candidates.Add(Math.Clamp(desiredStart, 0, maxPlacement));
        }

        for (var i = 0; i < occupied.Count - 1; i++)
        {
            var gapStart = occupied[i].End;
            var gapEnd = occupied[i + 1].Start;
            if (gapEnd - gapStart >= draggedDuration)
            {
                var maxPlacement = gapEnd - draggedDuration;
                candidates.Add(Math.Clamp(desiredStart, gapStart, maxPlacement));
            }
        }

        // Tail gap is unbounded — guarantees a valid placement always exists.
        var tailStart = occupied[^1].End;
        candidates.Add(Math.Max(tailStart, desiredStart));

        return candidates.OrderBy(c => Math.Abs(c - desiredStart)).First();
    }

    /// <summary>
    /// Resolves a rigid-group frame delta to the value closest to <paramref name="desiredDelta"/>
    /// that keeps every moved clip non-overlapping with the non-moved clips on its track and at or
    /// after frame 0. <paramref name="moved"/> carries each moved clip with its <em>original</em>
    /// start (deltas are relative to the gesture's start, not the live positions). The group keeps
    /// its internal spacing and shifts as one; like a single-clip move it may <em>jump over</em> a
    /// static clip into free space on the far side, settling on whichever fitting delta is nearest
    /// the drag (never rippling or overwriting — ADR 0006). Delta 0 (the original layout) is always
    /// a valid fallback. Single-clip moves use <see cref="Snap"/>; this is the multi-select path.
    /// </summary>
    public int ClampGroupDelta(IReadOnlyList<(Clip Clip, int OriginalStart)> moved, int desiredDelta)
    {
        if (moved is null || moved.Count == 0) return 0;

        var movedSet = new HashSet<Clip>(moved.Count);
        foreach (var m in moved) movedSet.Add(m.Clip);

        // Frame-0 floor: the earliest-starting moved clip can't be pushed before 0.
        var minStart = int.MaxValue;
        foreach (var (_, originalStart) in moved) minStart = Math.Min(minStart, originalStart);
        var lowerBound = -minStart;

        // Each (moved clip, static neighbour on the same track) pair forbids an open delta
        // interval: the moved span [s+d, e+d) overlaps the static [a, b) exactly when
        // d is in (a - e, b - s). Endpoints are allowed (edges touching is non-overlapping).
        var forbidden = new List<(int Lo, int Hi)>();
        foreach (var (clip, originalStart) in moved)
        {
            var track = FindTrack(clip);
            if (track is null) continue;

            var start = originalStart;
            var end = originalStart + Math.Max(1, clip.Duration);
            foreach (var other in track.Clips)
            {
                if (movedSet.Contains(other)) continue;   // static neighbours only
                var otherStart = other.TimelineStart;
                var otherEnd = other.TimelineStart + Math.Max(1, other.Duration);
                forbidden.Add((otherStart - end, otherEnd - start));
            }
        }

        bool IsValid(int d)
        {
            if (d < lowerBound) return false;
            foreach (var (lo, hi) in forbidden)
            {
                if (lo < d && d < hi) return false;
            }
            return true;
        }

        if (IsValid(desiredDelta)) return desiredDelta;

        // Otherwise the nearest valid delta sits on a constraint boundary: the frame-0 floor, the
        // original layout (delta 0, always overlap-free), or an edge of a forbidden interval.
        var best = 0;
        var bestDistance = Math.Abs((long)desiredDelta);
        void Consider(int d)
        {
            if (!IsValid(d)) return;
            var distance = Math.Abs((long)d - desiredDelta);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = d;
            }
        }

        Consider(lowerBound);
        foreach (var (lo, hi) in forbidden)
        {
            Consider(lo);
            Consider(hi);
        }
        return best;
    }

    /// <summary>
    /// Whether <paramref name="clip"/> may move onto <paramref name="target"/>: the target must
    /// be unlocked and of the same <see cref="TrackKind"/> as the clip's current track (#12
    /// cross-track rule — Streams are preserved, a different-kind target is refused).
    /// </summary>
    public bool CanMoveToTrack(Clip clip, Track target)
    {
        if (clip is null || target is null || target.Locked) return false;
        var current = FindTrack(clip);
        return current is not null && current.Kind == target.Kind;
    }

    /// <summary>The track that currently holds <paramref name="clip"/>, or null if none does.</summary>
    public Track? FindTrack(Clip clip)
    {
        foreach (var track in Project.Tracks)
        {
            if (track.Clips.Contains(clip)) return track;
        }
        return null;
    }

    /// <summary>
    /// Moves <paramref name="clip"/> to <paramref name="toTrack"/> at <paramref name="newStart"/>,
    /// removing it from whichever track currently holds it and re-inserting in TimelineStart order
    /// so the destination stays sorted. Idempotent w.r.t. the clip's current location, so the
    /// move-between-tracks command can drive both redo and undo through it.
    /// </summary>
    public void MoveClipToTrack(Clip clip, Track toTrack, int newStart)
    {
        var current = FindTrack(clip);
        current?.Clips.Remove(clip);
        clip.TimelineStart = Math.Max(0, newStart);
        InsertSorted(toTrack, clip);
    }

    private static void InsertSorted(Track track, Clip clip)
    {
        var insertIdx = track.Clips.Count;
        for (var i = 0; i < track.Clips.Count; i++)
        {
            if (track.Clips[i].TimelineStart > clip.TimelineStart)
            {
                insertIdx = i;
                break;
            }
        }
        track.Clips.Insert(insertIdx, clip);
    }

    /// <summary>
    /// Detaches the audio stream of a <see cref="ClipStreams.Both"/> <see cref="MediaClip"/>
    /// onto a freshly-appended audio track. No-op for any other clip shape (the menu item
    /// is greyed but still invokes through the visible-but-disabled pattern). The new track
    /// is named "Audio N" with N starting at 2 — the default "Audio" track from
    /// <see cref="Project.Project.CreateEmpty"/> is never reused, so split-off tracks pile up
    /// below the originals with predictable sequential numbers. The paired clip mirrors the
    /// source clip's <see cref="MediaClip.TimelineStart"/> / <see cref="MediaClip.SourceIn"/>
    /// / <see cref="MediaClip.SourceOut"/> / <see cref="MediaClip.MediaSourceId"/> but with
    /// <see cref="ClipStreams.Audio"/>; <see cref="MediaClip.DisplayName"/> then renders it
    /// as "&lt;source&gt; (Audio)" on the timeline so users can tell the audio half from the
    /// video half without inspecting the track. Always creates a new track — never reuses
    /// an existing audio track. Clips remain unlinked afterward (they move independently).
    /// Named "Detach audio" per CONTEXT.md — "Split" is reserved for the #12 razor operation.
    /// </summary>
    public void DetachAudio(MediaClip clip)
    {
        if (clip.Streams != ClipStreams.Both) return;

        var sourceTrack = FindTrack(clip);
        if (sourceTrack is null) return;

        // Resolve the source by id (per ADR 0003: never trust the denormalized Source ref).
        // Fallback to the local ref so a clip wired before id-lookup works (e.g. tests).
        var source = Project.MediaSources.FirstOrDefault(s => s.Id == clip.MediaSourceId)
                     ?? clip.Source;

        // Walk Audio 2, Audio 3, … picking the first name not already taken. Starts at 2
        // by convention — the bare "Audio" track is the default empty one and is never
        // overwritten even if the user has renamed it away.
        var n = 2;
        string trackName;
        do
        {
            trackName = $"Audio {n}";
            n++;
        } while (Project.Tracks.Any(t => string.Equals(t.Name, trackName, StringComparison.Ordinal)));

        // No literal Name stamp — MediaClip.DisplayName auto-derives "<source> (Audio)"
        // from the audio-half shape (Streams=Audio over a HasVideo source), so the label
        // tracks bin renames of the source. The user can override that with the rename
        // menu; if they do, the override freezes.
        var audioTrack = new Track { Kind = TrackKind.Audio, Name = trackName };
        audioTrack.Clips.Add(new MediaClip
        {
            MediaSourceId = clip.MediaSourceId,
            Source = source,
            SourceIn = clip.SourceIn,
            SourceOut = clip.SourceOut,
            Streams = ClipStreams.Audio,
            Framerate = clip.Framerate,
            TimelineStart = clip.TimelineStart,
        });

        Project.Tracks.Add(audioTrack);
        clip.Streams = ClipStreams.Video;
    }
}
