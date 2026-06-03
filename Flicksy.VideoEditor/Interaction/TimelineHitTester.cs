using System;
using System.Collections.Generic;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Interaction;

/// <summary>
/// Pure (WPF-free) cross-track hit-test math for the timeline interaction layer: maps a
/// content-space point to the <see cref="Track"/> / <see cref="Clip"/> / <see cref="HitZone"/>
/// / frame under it. Kept free of any WPF type so it unit-tests headless in
/// <c>Flicksy.VideoEditor.Tests</c> — the reason ADR 0007 abstracts the interaction layer at
/// all. <see cref="ITimelineSurface"/> implementations just unpack a <c>Point</c> into the
/// <see cref="HitTest"/> call.
/// <para>
/// Coordinate model (matches the timeline view): X is content pixels — frame
/// <c>= x / pixelsPerFrame</c>; Y is the vertical lane stack — track index
/// <c>= floor(y / trackHeight)</c> over <see cref="Project.Project.Tracks"/> in document order
/// (top track first), which is how <c>TimelineView</c> stacks its lanes.
/// </para>
/// </summary>
public static class TimelineHitTester
{
    /// <summary>
    /// Default edge grab width in screen pixels. The interactive edge band is this wide on
    /// each side of a clip, but never exceeds a third of the clip's width — so even a narrow
    /// clip keeps a grabbable middle-third body for Move.
    /// </summary>
    public const double DefaultEdgePixels = 5.0;

    /// <summary>
    /// Resolves <paramref name="x"/>/<paramref name="y"/> (content-space pixels) against
    /// <paramref name="tracks"/>. Returns <see cref="TimelineHit.Miss"/> when the point is
    /// above the first lane or below the last. <paramref name="edgePixels"/> is the per-side
    /// trim-edge band width in screen pixels (converted to frames via
    /// <paramref name="pixelsPerFrame"/>).
    /// <para>
    /// <c>Locked</c> tracks are inert (ADR 0006): the hit still resolves the track + frame
    /// (so a click on a locked lane can still clear selection / scrub), but never reports a
    /// <see cref="Clip"/> or an edge — <see cref="HitZone.None"/> is returned so no edit tool
    /// engages.
    /// </para>
    /// </summary>
    public static TimelineHit HitTest(
        double x,
        double y,
        IReadOnlyList<Track> tracks,
        double pixelsPerFrame,
        double trackHeight,
        double edgePixels = DefaultEdgePixels)
    {
        if (tracks is null) throw new ArgumentNullException(nameof(tracks));
        if (pixelsPerFrame <= 0 || trackHeight <= 0) return TimelineHit.Miss;
        if (y < 0) return TimelineHit.Miss;

        var trackIndex = (int)Math.Floor(y / trackHeight);
        if (trackIndex < 0 || trackIndex >= tracks.Count) return TimelineHit.Miss;

        var track = tracks[trackIndex];
        var frame = FrameAt(x, pixelsPerFrame);

        // Locked track: report the track + frame but never a clip or edge, so edit tools
        // (Move/Trim) find nothing to grab while scrub/clear-select still works.
        if (track.Locked)
        {
            return new TimelineHit(track, null, HitZone.None, frame);
        }

        // Find the clip whose painted pixel span [leftPx, rightPx) contains the X. Pixel-based
        // (not rounded-frame-based) so the hit matches exactly what ClipView draws — otherwise
        // a click in a clip's last sub-frame-pixel rounds up to the half-open end and misses.
        // Clips on a track never overlap (model invariant), so at most one matches.
        foreach (var clip in track.Clips)
        {
            var leftPx = clip.TimelineStart * pixelsPerFrame;
            var rightPx = leftPx + Math.Max(1, clip.Duration) * pixelsPerFrame;
            if (x < leftPx || x >= rightPx) continue;

            var zone = ResolveZone(x, leftPx, rightPx, edgePixels);
            return new TimelineHit(track, clip, zone, frame);
        }

        return new TimelineHit(track, null, HitZone.None, frame);
    }

    /// <summary>
    /// Returns every <see cref="Clip"/> whose painted span intersects the content-space rectangle
    /// (<paramref name="left"/> / <paramref name="top"/> / <paramref name="width"/> /
    /// <paramref name="height"/>) across all tracks — the marquee multi-select hit-test (#12 phase
    /// 6). <c>Locked</c> tracks are skipped (inert per ADR 0006). Results are track-major (top lane
    /// first) then left-to-right within a track, so the first entry is the natural primary
    /// selection. A zero-area rectangle selects nothing.
    /// <para>
    /// Horizontal containment uses the same half-open painted pixel span <see cref="HitTest"/> does
    /// (<c>[leftPx, rightPx)</c>); vertical containment uses the lane band
    /// (<c>[laneTop, laneBottom)</c> — the full <paramref name="trackHeight"/> row, not the clip's
    /// inset visual), so a band grazing a track row selects the clips it horizontally covers without
    /// pixel-precise vertical aim.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Clip> ClipsIntersecting(
        double left,
        double top,
        double width,
        double height,
        IReadOnlyList<Track> tracks,
        double pixelsPerFrame,
        double trackHeight)
    {
        if (tracks is null) throw new ArgumentNullException(nameof(tracks));

        var result = new List<Clip>();
        if (pixelsPerFrame <= 0 || trackHeight <= 0) return result;
        if (width <= 0 || height <= 0) return result;   // a zero-area band selects nothing

        var right = left + width;
        var bottom = top + height;

        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            if (track.Locked) continue;   // inert per ADR 0006 — never marquee-selectable

            var laneTop = i * trackHeight;
            var laneBottom = laneTop + trackHeight;
            if (bottom <= laneTop || top >= laneBottom) continue;   // no vertical overlap with this lane

            foreach (var clip in track.Clips)
            {
                var leftPx = clip.TimelineStart * pixelsPerFrame;
                var rightPx = leftPx + Math.Max(1, clip.Duration) * pixelsPerFrame;
                if (right <= leftPx || left >= rightPx) continue;   // no horizontal overlap with this clip
                result.Add(clip);
            }
        }

        return result;
    }

    /// <summary>
    /// Maps a content-space X to a timeline frame (rounded to nearest). Shared with the view's
    /// scrub math so a click resolves to the same frame the playhead would seek to.
    /// </summary>
    public static int FrameAt(double x, double pixelsPerFrame)
    {
        if (pixelsPerFrame <= 0) return 0;
        return Math.Max(0, (int)Math.Round(x / pixelsPerFrame));
    }

    private static HitZone ResolveZone(double x, double leftPx, double rightPx, double edgePixels)
    {
        // Clamp the grab band to a third of the clip so the two edge bands never meet: a
        // narrow clip keeps a middle-third body that resolves to Body (grabbable for Move).
        var band = Math.Min(edgePixels, (rightPx - leftPx) / 3.0);

        if (x <= leftPx + band) return HitZone.LeftEdge;
        if (x >= rightPx - band) return HitZone.RightEdge;
        return HitZone.Body;
    }
}
