using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Undo;
using Flicksy.Drawing.Undo.Commands;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.Undo;
using Flicksy.VideoEditor.Undo.Commands;

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

    // Floating-point guard when converting source-time headroom into whole-frame trim bounds:
    // a true integer that underflowed (e.g. 89.9999998) floors up correctly without inflating a
    // genuinely fractional value.
    private const double FrameEpsilon = 1e-6;

    // Guards the SelectedClip <-> SelectedClips sync so neither side's write re-triggers the
    // other. SetSelection drives the set and lets OnSelectedClipChanged skip its own rebuild;
    // a bare SelectedClip write (today's single-select click) rebuilds the set to match.
    private bool _syncingSelection;

    [ObservableProperty]
    private double pixelsPerFrame = 6.0;

    [ObservableProperty]
    private Clip? selectedClip;

    // Razor mode (#12 phase 5). While true, TimelineView engages the RazorTool as the router's
    // SelectedModeTool so a click cuts the clicked clip at the click point. Toggled by the C key /
    // the razor toggle button; split-at-playhead (S / scissor) is independent of this flag.
    [ObservableProperty]
    private bool isRazorMode;

    public TimelineViewModel(Project.Project project, TransportViewModel transport, UndoManager history)
    {
        Project = project;
        Transport = transport;
        History = history;

        // Keep the Split / Delete buttons' enabled state in step with the selection, so a click with
        // nothing to act on greys the button out instead of firing a silent no-op command.
        SelectedClips.CollectionChanged += (_, _) =>
        {
            SplitSelectedAtPlayheadCommand.NotifyCanExecuteChanged();
            DeleteSelectedCommand.NotifyCanExecuteChanged();
        };
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
        List<Clip> distinct = clips?.Distinct().ToList() ?? new List<Clip>();

        _syncingSelection = true;
        try
        {
            SelectedClips.Clear();
            foreach (Clip clip in distinct)
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
        if (_syncingSelection) 
            return;

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
        if (clip is null) 
            return;

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
    /// Removes <paramref name="clip"/> from the selection if present, promoting another member to
    /// primary (or clearing to empty), preserving the "primary is null iff the set is empty" invariant.
    /// No-op when the clip isn't selected. Used by delete-redo so a removed clip doesn't linger as the
    /// selection.
    /// </summary>
    public void Deselect(Clip clip)
    {
        if (clip is null || !SelectedClips.Contains(clip)) 
            return;

        _syncingSelection = true;
        try
        {
            SelectedClips.Remove(clip);
            if (ReferenceEquals(SelectedClip, clip))
            {
                SelectedClip = SelectedClips.Count > 0 ? SelectedClips[0] : null;
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
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor)) 
            return;

        PixelsPerFrame = Math.Clamp(PixelsPerFrame * factor, MinPixelsPerFrame, MaxPixelsPerFrame);
    }

    /// <summary>
    /// Sets <see cref="TransportViewModel.Playhead"/> to <paramref name="frame"/>, clamped
    /// to <c>[0, TotalFrames]</c>. Used by scrub gestures on the ruler and the empty lane
    /// area; clip-internal scrubbing (drag the playhead handle itself) is a later slice.
    /// </summary>
    public void SeekToFrame(int frame)
    {
        int max = Math.Max(0, Transport.TotalFrames);
        Transport.Playhead = Math.Clamp(frame, 0, max);
    }

    /// <summary>
    /// Convenience: convert a lane-relative pixel offset to a frame and seek there.
    /// </summary>
    public void SeekToPixel(double laneX)
    {
        if (PixelsPerFrame <= 0) 
            return;

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
        int frame = Math.Max(0, landingFrame);

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
        int frame = Math.Max(0, desiredStart);
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
        double snapRadiusFrames = SnapRadiusPixels / PixelsPerFrame;
        int best = frame;
        double bestDelta = snapRadiusFrames;

        void Consider(int candidate)
        {
            int delta = Math.Abs(candidate - frame);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = candidate;
            }
        }

        foreach (Track track in Project.Tracks)
        {
            foreach (Clip clip in track.Clips)
            {
                if (excludeClips is not null && excludeClips.Contains(clip)) 
                    continue;

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

        List<(int Start, int End)> occupied = targetTrack.Clips
            .Where(c => excludeClips is null || !excludeClips.Contains(c))
            .Select(c => (Start: c.TimelineStart, End: c.TimelineStart + Math.Max(1, c.Duration)))
            .OrderBy(i => i.Start)
            .ToList();

        if (occupied.Count == 0)
        {
            return Math.Max(0, desiredStart);
        }

        int desiredEnd = desiredStart + draggedDuration;
        bool overlaps = occupied.Any(i => desiredStart < i.End && i.Start < desiredEnd);
        if (!overlaps)
        {
            return Math.Max(0, desiredStart);
        }

        var candidates = new List<int>();

        int leadEnd = occupied[0].Start;
        if (leadEnd >= draggedDuration)
        {
            int maxPlacement = leadEnd - draggedDuration;
            candidates.Add(Math.Clamp(desiredStart, 0, maxPlacement));
        }

        for (int i = 0; i < occupied.Count - 1; i++)
        {
            int gapStart = occupied[i].End;
            int gapEnd = occupied[i + 1].Start;
            if (gapEnd - gapStart >= draggedDuration)
            {
                int maxPlacement = gapEnd - draggedDuration;
                candidates.Add(Math.Clamp(desiredStart, gapStart, maxPlacement));
            }
        }

        // Tail gap is unbounded — guarantees a valid placement always exists.
        int tailStart = occupied[^1].End;
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
        if (moved is null || moved.Count == 0) 
            return 0;

        var movedSet = new HashSet<Clip>(moved.Count);
        foreach ((Clip clip, _) in moved) 
            movedSet.Add(clip);

        // Frame-0 floor: the earliest-starting moved clip can't be pushed before 0.
        var minStart = int.MaxValue;
        foreach ((_, int originalStart) in moved) 
            minStart = Math.Min(minStart, originalStart);

        int lowerBound = -minStart;

        // Each (moved clip, static neighbour on the same track) pair forbids an open delta
        // interval: the moved span [s+d, e+d) overlaps the static [a, b) exactly when
        // d is in (a - e, b - s). Endpoints are allowed (edges touching is non-overlapping).
        var forbidden = new List<(int Lo, int Hi)>();
        foreach ((Clip clip, int originalStart) in moved)
        {
            Track? track = FindTrack(clip);
            if (track is null) 
                continue;

            int start = originalStart;
            int end = originalStart + Math.Max(1, clip.Duration);
            foreach (Clip other in track.Clips)
            {
                if (movedSet.Contains(other)) 
                    continue;   // static neighbours only

                int otherStart = other.TimelineStart;
                int otherEnd = other.TimelineStart + Math.Max(1, other.Duration);
                forbidden.Add((otherStart - end, otherEnd - start));
            }
        }

        bool IsValid(int d)
        {
            if (d < lowerBound) 
                return false;

            foreach ((int lo, int hi) in forbidden)
            {
                if (lo < d && d < hi) 
                    return false;
            }
            return true;
        }

        if (IsValid(desiredDelta)) 
            return desiredDelta;

        // Otherwise the nearest valid delta sits on a constraint boundary: the frame-0 floor, the
        // original layout (delta 0, always overlap-free), or an edge of a forbidden interval.
        int best = 0;
        long bestDistance = Math.Abs((long)desiredDelta);

        void Consider(int d)
        {
            if (!IsValid(d)) 
                return;

            long distance = Math.Abs((long)d - desiredDelta);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = d;
            }
        }

        Consider(lowerBound);
        foreach ((int lo, int hi) in forbidden)
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
        if (clip is null || target is null || target.Locked) 
            return false;

        Track? current = FindTrack(clip);
        return current is not null && current.Kind == target.Kind;
    }

    /// <summary>The track that currently holds <paramref name="clip"/>, or null if none does.</summary>
    public Track? FindTrack(Clip clip)
    {
        foreach (Track track in Project.Tracks)
        {
            if (track.Clips.Contains(clip)) 
                return track;
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
        Track? current = FindTrack(clip);
        current?.Clips.Remove(clip);
        clip.TimelineStart = Math.Max(0, newStart);
        InsertSorted(toTrack, clip);
    }

    /// <summary>
    /// Re-inserts <paramref name="clip"/> into <paramref name="track"/> in TimelineStart order
    /// without disturbing the other clips' sort. Used by the split / delete undo commands to restore
    /// a removed clip (its TimelineStart is unchanged, so it lands back exactly where it was).
    /// </summary>
    public void InsertClipSorted(Track track, Clip clip) => InsertSorted(track, clip);

    private static void InsertSorted(Track track, Clip clip)
    {
        int insertIdx = track.Clips.Count;
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
    /// Resolves a single-edge trim of <paramref name="clip"/>, returning the clamped
    /// <see cref="TrimResult"/> (the gesture applies it live; <c>TrimClipCommand</c> records
    /// before/after). Dragging the <paramref name="fromLeftEdge"/> edge to
    /// <paramref name="desiredEdgeFrame"/> — the desired new start when trimming the left edge, the
    /// desired new end when trimming the right — maps the timeline delta into source time via the
    /// clip's <see cref="MediaClip.Speed"/>, holding the opposite edge fixed (so
    /// <see cref="MediaClip.Duration"/> recomputes to match). Clamped, in order, by: the
    /// neighbouring clip's edge on the same track (trim never ripples a neighbour — ADR 0006), the
    /// source bounds (<c>SourceIn &gt;= 0</c>, <c>SourceOut &lt;= Source.Duration</c>), and a
    /// 1-frame minimum duration. A broken / missing-source clip (<see cref="MediaClip.IsBroken"/>)
    /// may shrink but not extend, since its true source length can't be trusted. Single-clip only —
    /// trim never operates on the whole selection.
    /// </summary>
    public TrimResult ResolveTrim(MediaClip clip, bool fromLeftEdge, int desiredEdgeFrame)
    {
        int start = clip.TimelineStart;
        int duration = clip.Duration;

        // A degenerate (zero-length) clip has no edge worth trimming; leaving it untouched also
        // keeps the clamps below from inverting (lower > upper).
        if (duration < 1) 
            return TrimResult.Capture(clip);

        int end = start + duration;
        Track? track = FindTrack(clip);
        bool canExtend = !clip.IsBroken;
        MediaSource? source = Project.MediaSources.FirstOrDefault(s => s.Id == clip.MediaSourceId) ?? clip.Source;

        // Source seconds spanned by one timeline frame at this clip's speed. Speed and Framerate
        // are both > 0 here (otherwise Duration would be 0 and we returned above).
        double sourcePerFrame = clip.Speed / clip.Framerate;

        if (fromLeftEdge)
        {
            // Left (in-point): the start slides, the right edge stays put, SourceIn shifts with it.
            int upper = end - 1;                                   // 1-frame minimum
            int lower = Math.Max(0, PrevClipEnd(track, clip, start));
            if (!canExtend)
            {
                lower = Math.Max(lower, start);                    // shrink only
            }
            else
            {
                // SourceIn can't drop below zero: cap how far left the start may slide.
                int maxLeftFrames = (int)Math.Floor(clip.SourceIn.TotalSeconds / sourcePerFrame + FrameEpsilon);
                lower = Math.Max(lower, start - maxLeftFrames);
            }

            int newStart = Math.Clamp(desiredEdgeFrame, lower, upper);
            TimeSpan newSourceIn = clip.SourceIn + TimeSpan.FromSeconds((newStart - start) * sourcePerFrame);
            if (newSourceIn < TimeSpan.Zero) 
                newSourceIn = TimeSpan.Zero;   // float guard

            return new TrimResult(newStart, newSourceIn, clip.SourceOut);
        }
        else
        {
            // Right (out-point): the end slides, start + SourceIn stay put, SourceOut shifts.
            int lower = start + 1;                                 // 1-frame minimum
            int upper = NextClipStart(track, clip, end);           // neighbour, or no timeline cap
            if (!canExtend)
            {
                upper = Math.Min(upper, end);                      // shrink only
            }
            else if (source is not null)
            {
                // SourceOut can't exceed the source length: cap how far right the end may slide.
                int headroomFrames = (int)Math.Floor((source.Duration - clip.SourceOut).TotalSeconds / sourcePerFrame + FrameEpsilon);
                upper = Math.Min(upper, end + Math.Max(0, headroomFrames));
            }

            int newEnd = Math.Clamp(desiredEdgeFrame, lower, upper);
            TimeSpan newSourceOut = clip.SourceOut + TimeSpan.FromSeconds((newEnd - end) * sourcePerFrame);

            if (source is not null && newSourceOut > source.Duration) 
                newSourceOut = source.Duration;   // float guard

            return new TrimResult(start, clip.SourceIn, newSourceOut);
        }
    }

    // Greatest end frame among clips entirely left of `start` on the track (clips never overlap, so
    // every other clip is wholly left or wholly right). Frame 0 when there's no left neighbour — the
    // left edge can't be trimmed before the track origin regardless.
    private static int PrevClipEnd(Track? track, Clip clip, int start)
    {
        if (track is null) 
            return 0;

        int best = 0;
        foreach (var c in track.Clips)
        {
            if (ReferenceEquals(c, clip)) 
                continue;

            int cEnd = c.TimelineStart + Math.Max(1, c.Duration);

            if (cEnd <= start && cEnd > best) 
                best = cEnd;
        }
        return best;
    }

    // Smallest start frame among clips entirely right of `end` on the track, or int.MaxValue when
    // there's no right neighbour (no timeline cap — the source bound then governs).
    private static int NextClipStart(Track? track, Clip clip, int end)
    {
        if (track is null) 
            return int.MaxValue;

        var best = int.MaxValue;
        foreach (Clip c in track.Clips)
        {
            if (ReferenceEquals(c, clip)) 
                continue;

            int cStart = c.TimelineStart;
            if (cStart >= end && cStart < best) 
                best = cStart;
        }
        return best;
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
    /// Reversible as one undo step bundling an <see cref="AddTrackCommand"/>, an
    /// <see cref="AddClipCommand"/>, and a <see cref="ChangeClipStreamsCommand"/> — granular so a
    /// future detach onto an existing track can reuse the latter two and drop the track add.
    /// </summary>
    public void DetachAudio(MediaClip clip)
    {
        if (clip.Streams != ClipStreams.Both) 
            return;

        Track? sourceTrack = FindTrack(clip);
        if (sourceTrack is null || sourceTrack.Locked) 
            return;   // locked tracks are inert (ADR 0006)

        // Resolve the source by id (per ADR 0003: never trust the denormalized Source ref).
        // Fallback to the local ref so a clip wired before id-lookup works (e.g. tests).
        MediaSource? source = Project.MediaSources.FirstOrDefault(s => s.Id == clip.MediaSourceId)
                     ?? clip.Source;

        // Walk Audio 2, Audio 3, … picking the first name not already taken. Starts at 2
        // by convention — the bare "Audio" track is the default empty one and is never
        // overwritten even if the user has renamed it away.
        int n = 2;
        string trackName;
        do
        {
            trackName = $"Audio {n}";
            n++;
        } 
        while (Project.Tracks.Any(t => string.Equals(t.Name, trackName, StringComparison.Ordinal)));

        // No literal Name stamp — MediaClip.DisplayName auto-derives "<source> (Audio)"
        // from the audio-half shape (Streams=Audio over a HasVideo source), so the label
        // tracks bin renames of the source. The user can override that with the rename
        // menu; if they do, the override freezes.
        var audioTrack = new Track { Kind = TrackKind.Audio, Name = trackName };
        var audioClip = new MediaClip
        {
            MediaSourceId = clip.MediaSourceId,
            Source = source,
            SourceIn = clip.SourceIn,
            SourceOut = clip.SourceOut,
            Streams = ClipStreams.Audio,
            Framerate = clip.Framerate,
            TimelineStart = clip.TimelineStart,
        };

        // Mutate live (push-after-mutate convention), then bundle the inverse of each step into one
        // undo entry. Three granular commands rather than a single "detach" command so a future
        // detach-onto-an-existing-track can keep the clip add + stream flip and drop the track add.
        int trackIndex = Project.Tracks.Count;   // appended at the end
        Project.Tracks.Add(audioTrack);
        audioTrack.Clips.Add(audioClip);
        clip.Streams = ClipStreams.Video;

        PushBundle(new IUndoableCommand[]
        {
            new AddTrackCommand(this, audioTrack, trackIndex),
            new AddClipCommand(this, audioTrack, audioClip),
            new ChangeClipStreamsCommand(clip, ClipStreams.Both, ClipStreams.Video),
        });
    }

    /// <summary>
    /// Adds an already-constructed <paramref name="clip"/> to <paramref name="track"/> as one undoable
    /// edit — the model side of a media-bin drag-drop (<c>ClipsLaneView</c> builds the clip from the
    /// drop's stream resolution + snapped landing frame, then calls this). Inserts in TimelineStart
    /// order, selects the new clip, and pushes an <see cref="AddClipCommand"/> so the drop is reversible.
    /// Owning the mutation here (rather than in the lane's drop handler) keeps it unit-testable, mirroring
    /// split / delete.
    /// </summary>
    public void AddClip(Track track, Clip clip)
    {
        InsertSorted(track, clip);
        SelectedClip = clip;
        History.Push(new AddClipCommand(this, track, clip));
    }

    /// <summary>
    /// Adds a new empty <see cref="Track"/> of <paramref name="kind"/> to the project as one undoable
    /// edit — the corner Add-track button's model entry. The track is named by <see cref="NextTrackName"/>
    /// and inserted by <see cref="ResolveInsertIndex"/> so it lands at the bottom of its kind's group
    /// (tracks stay ordered Video → Overlay → Audio, matching the timeline UI and the compositor's
    /// z-grouping). Selection is untouched — tracks aren't selectable, only clips are.
    /// </summary>
    [RelayCommand]
    private void AddTrack(TrackKind kind)
    {
        var track = new Track { Kind = kind, Name = NextTrackName(kind) };
        int index = ResolveInsertIndex(kind);
        Project.Tracks.Insert(index, track);
        History.Push(new AddTrackCommand(this, track, index));
    }

    /// <summary>
    /// Removes <paramref name="track"/> from the project as one undoable edit — the model entry for the
    /// track header's "Delete track" command (the View owns the confirm-if-non-empty prompt, so this
    /// stays headless-testable). Any selected clip living on the track is dropped from the selection
    /// first so the right rail / inspector don't dangle on a clip that's no longer in the document. The
    /// removed <see cref="Track"/> instance keeps its clips while detached, so the pushed
    /// <see cref="RemoveTrackCommand"/> restores them on undo by re-inserting the same instance. No-op
    /// when the track isn't in the project. Allowed on locked / last tracks alike (a locked track guards
    /// its clips' edits, not the track itself; an empty timeline is recoverable via Add track).
    /// </summary>
    public void RemoveTrack(Track track)
    {
        int index = Project.Tracks.IndexOf(track);
        if (index < 0)
            return;

        List<Clip> remaining = SelectedClips.Where(c => !track.Clips.Contains(c)).ToList();
        if (remaining.Count != SelectedClips.Count)
            SetSelection(remaining);

        Project.Tracks.Remove(track);
        History.Push(new RemoveTrackCommand(this, track, index));
    }

    // The index a new track of `kind` is inserted at to keep tracks grouped Video → Overlay → Audio
    // (the order TrackKind is declared in, which matches the timeline's top-to-bottom layout and the
    // compositor's z-grouping). Insert just before the first existing track of a higher kind, so a new
    // track lands at the bottom of its own kind's contiguous group; appended at the end when none are
    // higher (e.g. a new Audio track).
    private int ResolveInsertIndex(TrackKind kind)
    {
        for (int i = 0; i < Project.Tracks.Count; i++)
        {
            if ((int)Project.Tracks[i].Kind > (int)kind)
                return i;
        }
        return Project.Tracks.Count;
    }

    // First track name of `kind` not already taken. Video is always numbered from 1 ("Video 1",
    // "Video 2", …) to match CreateEmpty's defaults; Overlay / Audio use the bare base name first
    // ("Overlay", "Audio") then number from 2 (so a freshly-added Audio track on a project that has
    // none is just "Audio", a second is "Audio 2" — mirroring DetachAudio's piling). DetachAudio keeps
    // its own start-at-2 loop deliberately (it never reuses the bare default).
    private string NextTrackName(TrackKind kind)
    {
        string baseName = kind switch
        {
            TrackKind.Video => "Video",
            TrackKind.Overlay => "Overlay",
            TrackKind.Audio => "Audio",
            _ => "Track",
        };

        bool Taken(string name) => Project.Tracks.Any(t => string.Equals(t.Name, name, StringComparison.Ordinal));

        if (kind != TrackKind.Video && !Taken(baseName))
            return baseName;

        int n = kind == TrackKind.Video ? 1 : 2;
        while (Taken($"{baseName} {n}"))
            n++;

        return $"{baseName} {n}";
    }

    /// <summary>
    /// Splits <paramref name="clip"/> at <paramref name="frame"/> — the razor's cut-at-click entry
    /// (<c>RazorTool</c> calls this). No-op unless the frame is strictly inside the clip on an
    /// unlocked track. Pushes a single <c>SplitClipCommand</c> and selects the left half (the original).
    /// </summary>
    public void SplitClipAt(MediaClip clip, int frame)
    {
        var command = CreateSplit(clip, frame);
        if (command is null) 
            return;

        History.Push(command);
        SelectedClip = clip;   // the original is now the left half
    }

    /// <summary>
    /// Splits every selected <see cref="MediaClip"/> the playhead strictly passes through, at the
    /// playhead frame (the <c>S</c> key / scissor button). Non-MediaClips, clips on locked tracks,
    /// and clips the playhead sits at or outside the edges of are skipped (no zero-length halves).
    /// The originally-selected clips stay selected (each as its left half). Bundles multiple splits
    /// in a <c>CompositeCommand</c>; a no-op when nothing splits.
    /// </summary>
    private bool CanSplitSelected() => SelectedClips.Any(c => c is MediaClip);

    [RelayCommand(CanExecute = nameof(CanSplitSelected))]
    private void SplitSelectedAtPlayhead()
    {
        int frame = Transport.Playhead;
        var commands = new List<IUndoableCommand>();
        foreach (MediaClip clip in SelectedClips.OfType<MediaClip>().ToList())
        {
            SplitClipCommand? command = CreateSplit(clip, frame);
            if (command is not null) 
                commands.Add(command);
        }
        PushBundle(commands);
    }

    /// <summary>
    /// Deletes every selected <see cref="Clip"/> on an unlocked track (generic — Media and Graphics
    /// alike), leaving each vacated span as a gap (non-destructive — ADR 0006). Any transition a
    /// deleted clip participated in is removed with it. Clears the selection and bundles multiple
    /// deletes in a <c>CompositeCommand</c>; a no-op when nothing deletes (e.g. an all-locked selection).
    /// </summary>
    private bool CanDeleteSelected() => SelectedClips.Count > 0;

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private void DeleteSelected()
    {
        var commands = new List<IUndoableCommand>();
        foreach (Clip clip in SelectedClips.ToList())
        {
            var track = FindTrack(clip);
            if (track is null || track.Locked) 
                continue;   // locked tracks are inert (ADR 0006)

            List<Transition> before = track.Transitions.ToList();
            track.RemoveTransitionsFor(clip);
            track.Clips.Remove(clip);

            List<Transition> after = track.Transitions.ToList();
            commands.Add(new RemoveClipCommand(this, track, clip, before, after));
        }

        if (commands.Count == 0) 
            return;

        SetSelection(Array.Empty<Clip>());
        PushBundle(commands);
    }

    // Performs the split mutation for one clip and returns the (already-applied) command, or null
    // when the clip can't split here. The original is kept as the left half, so right-edge transitions
    // reassign to the new right half while left-edge ones stay put (Track.ReassignTransitionsForSplit).
    private SplitClipCommand? CreateSplit(MediaClip clip, int frame)
    {
        Track? track = FindTrack(clip);
        if (track is null || track.Locked) 
            return null;

        int start = clip.TimelineStart;
        int duration = clip.Duration;
        if (frame <= start || frame >= start + duration) 
            return null;   // strictly inside → both halves >= 1 frame

        TimeSpan sourceOutBefore = clip.SourceOut;

        // Source time at the split frame via the speed mapping (matches CompositionPlanner.ComputeSourceTime).
        int elapsedFrames = frame - start;
        TimeSpan splitSourceTime = clip.SourceIn + TimeSpan.FromSeconds(elapsedFrames * clip.Speed / clip.Framerate);

        var right = new MediaClip
        {
            MediaSourceId = clip.MediaSourceId,
            Source = clip.Source,
            SourceIn = splitSourceTime,
            SourceOut = sourceOutBefore,
            Speed = clip.Speed,
            Volume = clip.Volume,
            Streams = clip.Streams,
            Framerate = clip.Framerate,
            Name = clip.Name,
            TimelineStart = frame,
        };

        right.Transform.CopyFrom(clip.Transform);
        foreach (Filter filter in clip.Filters) 
            right.Filters.Add(filter);

        List<Transition> transitionsBefore = track.Transitions.ToList();
        clip.SourceOut = splitSourceTime;        // shrink the original into the left half (Duration recomputes)
        InsertSorted(track, right);
        track.ReassignTransitionsForSplit(clip, clip, right);
        List<Transition> transitionsAfter = track.Transitions.ToList();

        return new SplitClipCommand(this, track, clip, right, sourceOutBefore, splitSourceTime, transitionsBefore, transitionsAfter);
    }

    // Pushes a single command directly, or bundles several into one CompositeCommand undo step with a
    // TimelineSelectionScope so the whole selection survives the multi-step undo/redo.
    private void PushBundle(IReadOnlyList<IUndoableCommand> commands)
    {
        if (commands.Count == 0) 
            return;

        History.Push(commands.Count == 1
            ? commands[0]
            : new CompositeCommand(commands, new TimelineSelectionScope(this)));
    }
}
