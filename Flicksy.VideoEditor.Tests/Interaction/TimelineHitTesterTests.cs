using System;
using System.Collections.Generic;
using Flicksy.VideoEditor.Interaction;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Tests.Interaction;

[TestFixture]
public class TimelineHitTesterTests
{
    private const int Framerate = 30;
    private const double Ppf = 6.0;          // pixels per frame
    private const double TrackHeight = 56.0;
    private const double EdgePx = 5.0;

    // ---- Y → track mapping --------------------------------------------------

    [Test]
    public void HitTest_FirstLane_ResolvesFirstTrack()
    {
        List<Track> tracks = Tracks(TrackKind.Video, TrackKind.Audio);
        TimelineHit hit = Hit(x: 0, y: 10, tracks);
        Assert.That(hit.Track, Is.SameAs(tracks[0]));
    }

    [Test]
    public void HitTest_SecondLane_ResolvesSecondTrack()
    {
        List<Track> tracks = Tracks(TrackKind.Video, TrackKind.Audio);
        // y in [56, 112) is the second lane.
        TimelineHit hit = Hit(x: 0, y: TrackHeight + 1, tracks);
        Assert.That(hit.Track, Is.SameAs(tracks[1]));
    }

    [Test]
    public void HitTest_NegativeY_IsMiss()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        Assert.That(Hit(x: 0, y: -1, tracks), Is.EqualTo(TimelineHit.Miss));
    }

    [Test]
    public void HitTest_PastLastLane_IsMiss()
    {
        List<Track> tracks = Tracks(TrackKind.Video, TrackKind.Audio);
        // Two lanes occupy [0, 112); y beyond that is empty space below the stack.
        TimelineHit hit = Hit(x: 0, y: TrackHeight * 2 + 5, tracks);
        Assert.That(hit, Is.EqualTo(TimelineHit.Miss));
    }

    // ---- X → frame ----------------------------------------------------------

    [Test]
    public void HitTest_FrameIsRoundedFromX()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        // x = 5px at 6px/frame ≈ 0.83 → rounds to frame 1.
        Assert.That(Hit(x: 5, y: 10, tracks).Frame, Is.EqualTo(1));
        // x = 60px → frame 10 exactly.
        Assert.That(Hit(x: 60, y: 10, tracks).Frame, Is.EqualTo(10));
    }

    // ---- clip body vs empty -------------------------------------------------

    [Test]
    public void HitTest_OverClipBody_ReturnsBody()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 2)); // [10, 70)
        // Frame 40 (x = 240px) is mid-clip, well away from either edge band.
        TimelineHit hit = Hit(x: 40 * Ppf, y: 10, tracks);
        Assert.That(hit.Zone, Is.EqualTo(HitZone.Body));
        Assert.That(hit.Clip, Is.SameAs(tracks[0].Clips[0]));
    }

    [Test]
    public void HitTest_EmptyLaneSpace_ReturnsNoneAndNoClip()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 2)); // [10, 70)
        // Frame 5 (x = 30px) is before the clip — empty space.
        TimelineHit hit = Hit(x: 5 * Ppf, y: 10, tracks);
        Assert.That(hit.Zone, Is.EqualTo(HitZone.None));
        Assert.That(hit.Clip, Is.Null);
        Assert.That(hit.Track, Is.SameAs(tracks[0]));
    }

    [Test]
    public void HitTest_PastClipEnd_IsHalfOpen()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        tracks[0].Clips.Add(MediaClip(timelineStart: 0, sourceSeconds: 1)); // [0, 30)
        // Frame 30 is the first frame past the clip (half-open) → empty.
        TimelineHit hit = Hit(x: 30 * Ppf, y: 10, tracks);
        Assert.That(hit.Clip, Is.Null);
    }

    // ---- edge zones ---------------------------------------------------------

    [Test]
    public void HitTest_NearLeftEdge_ReturnsLeftEdge()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 2)); // left px = 60
        // 2px right of the left edge → within the 5px band.
        TimelineHit hit = Hit(x: 60 + 2, y: 10, tracks);
        Assert.That(hit.Zone, Is.EqualTo(HitZone.LeftEdge));
    }

    [Test]
    public void HitTest_NearRightEdge_ReturnsRightEdge()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 2)); // right px = 70*6 = 420
        TimelineHit hit = Hit(x: 420 - 2, y: 10, tracks);
        Assert.That(hit.Zone, Is.EqualTo(HitZone.RightEdge));
    }

    [Test]
    public void HitTest_TinyClip_EdgeBandClampedToHalfWidth_LeavesBodyGrabbable()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        // A 1-frame clip at 6px/frame is 6px wide. Band clamps to 3px each side, so the
        // exact center (x = leftPx + 3) is neither edge — it must resolve to Body so a
        // narrow clip can still be grabbed for Move.
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 1.0 / Framerate)); // 1 frame
        double leftPx = 10 * Ppf;
        TimelineHit hit = Hit(x: leftPx + 3, y: 10, tracks);
        Assert.That(hit.Zone, Is.EqualTo(HitZone.Body));
    }

    // ---- locked tracks are inert (ADR 0006) ---------------------------------

    [Test]
    public void HitTest_LockedTrack_ReportsTrackAndFrameButNoClip()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        tracks[0].Locked = true;
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 2));
        TimelineHit hit = Hit(x: 40 * Ppf, y: 10, tracks);
        Assert.That(hit.Track, Is.SameAs(tracks[0]), "track still resolves (scrub/clear-select works)");
        Assert.That(hit.Clip, Is.Null, "no clip reported on a locked track");
        Assert.That(hit.Zone, Is.EqualTo(HitZone.None));
        Assert.That(hit.Frame, Is.EqualTo(40));
    }

    // ---- degenerate inputs --------------------------------------------------

    [Test]
    public void HitTest_NonPositivePixelsPerFrame_IsMiss()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        Assert.That(TimelineHitTester.HitTest(10, 10, tracks, 0, TrackHeight), Is.EqualTo(TimelineHit.Miss));
    }

    [Test]
    public void HitTest_NullTracks_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TimelineHitTester.HitTest(0, 0, null!, Ppf, TrackHeight));
    }

    // ---- ClipsIntersecting (marquee, #12 phase 6) ---------------------------
    // Layout shared by these cases: clip A on lane 0 (Video) spans px [60, 420);
    // clip B on lane 1 (Audio) spans px [600, 780). Lane 0 Y = [0, 56), lane 1 Y = [56, 112).

    [Test]
    public void ClipsIntersecting_BandSpanningBothLanes_SelectsClipsOnBoth()
    {
        (List<Track> tracks, Clip a, Clip b) = TwoTrackLayout();
        // X [0, 700) covers A's start and reaches into B; Y [10, 70) straddles both lanes.
        IReadOnlyList<Clip> hits = Intersect(left: 0, top: 10, width: 700, height: 60, tracks);
        Assert.That(hits, Is.EqualTo(new Clip[] { a, b }));
    }

    [Test]
    public void ClipsIntersecting_BandInFirstLaneOnly_SelectsOnlyFirstTrackClip()
    {
        (List<Track> tracks, Clip a, _) = TwoTrackLayout();
        // Y [10, 50) stays inside lane 0, so B on lane 1 is excluded even though X reaches it.
        IReadOnlyList<Clip> hits = Intersect(left: 0, top: 10, width: 700, height: 40, tracks);
        Assert.That(hits, Is.EqualTo(new Clip[] { a }));
    }

    [Test]
    public void ClipsIntersecting_BandLeftOfEveryClip_SelectsNothing()
    {
        (List<Track> tracks, _, _) = TwoTrackLayout();
        // X [0, 50) ends before A's left px (60); nothing overlaps horizontally.
        IReadOnlyList<Clip> hits = Intersect(left: 0, top: 0, width: 50, height: 112, tracks);
        Assert.That(hits, Is.Empty);
    }

    [Test]
    public void ClipsIntersecting_PartialHorizontalOverlap_Selects()
    {
        (List<Track> tracks, Clip a, _) = TwoTrackLayout();
        // Band covers only A's first few pixels [55, 90) — partial overlap still selects.
        IReadOnlyList<Clip> hits = Intersect(left: 55, top: 10, width: 35, height: 30, tracks);
        Assert.That(hits, Is.EqualTo(new Clip[] { a }));
    }

    [Test]
    public void ClipsIntersecting_HalfOpenAtClipStart_Excludes()
    {
        (List<Track> tracks, _, _) = TwoTrackLayout();
        // Band right edge exactly at A's left px (60): [leftPx, rightPx) is half-open, so a band
        // ending at the start pixel doesn't grab it (consistent with HitTest's pixel model).
        IReadOnlyList<Clip> hits = Intersect(left: 0, top: 10, width: 60, height: 30, tracks);
        Assert.That(hits, Is.Empty);
        // One pixel further and it's inside.
        Assert.That(Intersect(left: 0, top: 10, width: 61, height: 30, tracks), Has.Count.EqualTo(1));
    }

    [Test]
    public void ClipsIntersecting_HalfOpenAtClipEnd_Excludes()
    {
        (List<Track> tracks, _, _) = TwoTrackLayout();
        // Band left edge exactly at A's right px (420): a band starting at the end pixel misses it.
        IReadOnlyList<Clip> hits = Intersect(left: 420, top: 10, width: 100, height: 30, tracks);
        Assert.That(hits, Is.Empty);
    }

    [Test]
    public void ClipsIntersecting_LockedTrack_IsSkipped()
    {
        (List<Track> tracks, _, Clip b) = TwoTrackLayout();
        tracks[0].Locked = true;   // lane 0 (clip A) is inert
        // A band covering both lanes now returns only B — A's locked lane is skipped.
        IReadOnlyList<Clip> hits = Intersect(left: 0, top: 10, width: 700, height: 60, tracks);
        Assert.That(hits, Is.EqualTo(new Clip[] { b }));
    }

    [Test]
    public void ClipsIntersecting_DisabledTrack_IsStillSelectable()
    {
        (List<Track> tracks, Clip a, _) = TwoTrackLayout();
        tracks[0].Disabled = true;   // Disabled is compositor-skip only — still editable / selectable
        IReadOnlyList<Clip> hits = Intersect(left: 0, top: 10, width: 700, height: 40, tracks);
        Assert.That(hits, Is.EqualTo(new Clip[] { a }));
    }

    [Test]
    public void ClipsIntersecting_OrdersTrackMajorThenLeftToRight()
    {
        List<Track> tracks = Tracks(TrackKind.Video);
        MediaClip right = MediaClip(timelineStart: 100, sourceSeconds: 1);   // px [600, 780)
        MediaClip left = MediaClip(timelineStart: 10, sourceSeconds: 1);     // px [60, 240)
        tracks[0].Clips.Add(right);                                    // added out of order
        tracks[0].Clips.Add(left);
        // Within a track the result follows Clips order; this asserts the method preserves it
        // (the primary picker downstream takes result[0]).
        IReadOnlyList<Clip> hits = Intersect(left: 0, top: 10, width: 800, height: 40, tracks);
        Assert.That(hits, Is.EqualTo(new Clip[] { right, left }));
    }

    [Test]
    public void ClipsIntersecting_ZeroAreaRect_SelectsNothing()
    {
        (List<Track> tracks, _, _) = TwoTrackLayout();
        Assert.That(Intersect(left: 100, top: 10, width: 0, height: 30, tracks), Is.Empty);
        Assert.That(Intersect(left: 100, top: 10, width: 30, height: 0, tracks), Is.Empty);
    }

    [Test]
    public void ClipsIntersecting_NonPositivePixelsPerFrame_IsEmpty()
    {
        (List<Track> tracks, _, _) = TwoTrackLayout();
        Assert.That(
            TimelineHitTester.ClipsIntersecting(0, 0, 700, 112, tracks, 0, TrackHeight),
            Is.Empty);
    }

    [Test]
    public void ClipsIntersecting_NullTracks_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => TimelineHitTester.ClipsIntersecting(0, 0, 10, 10, null!, Ppf, TrackHeight));
    }

    // ---- helpers ------------------------------------------------------------

    private static TimelineHit Hit(double x, double y, IReadOnlyList<Track> tracks) =>
        TimelineHitTester.HitTest(x, y, tracks, Ppf, TrackHeight, EdgePx);

    private static IReadOnlyList<Clip> Intersect(
        double left, double top, double width, double height, IReadOnlyList<Track> tracks) =>
        TimelineHitTester.ClipsIntersecting(left, top, width, height, tracks, Ppf, TrackHeight);

    // Clip A on lane 0 (Video) spans px [60, 420); clip B on lane 1 (Audio) spans px [600, 780).
    private static (List<Track> Tracks, Clip A, Clip B) TwoTrackLayout()
    {
        List<Track> tracks = Tracks(TrackKind.Video, TrackKind.Audio);
        MediaClip a = MediaClip(timelineStart: 10, sourceSeconds: 2);    // [10, 70) frames → px [60, 420)
        MediaClip b = MediaClip(timelineStart: 100, sourceSeconds: 1);   // [100, 130) frames → px [600, 780)
        tracks[0].Clips.Add(a);
        tracks[1].Clips.Add(b);
        return (tracks, a, b);
    }

    private static List<Track> Tracks(params TrackKind[] kinds)
    {
        var list = new List<Track>();
        foreach (TrackKind kind in kinds)
        {
            list.Add(new Track { Kind = kind });
        }
        return list;
    }

    private static MediaClip MediaClip(int timelineStart, double sourceSeconds) => new()
    {
        TimelineStart = timelineStart,
        SourceIn = TimeSpan.Zero,
        SourceOut = TimeSpan.FromSeconds(sourceSeconds),
        Framerate = Framerate,
    };
}
