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
        var tracks = Tracks(TrackKind.Video, TrackKind.Audio);
        var hit = Hit(x: 0, y: 10, tracks);
        Assert.That(hit.Track, Is.SameAs(tracks[0]));
    }

    [Test]
    public void HitTest_SecondLane_ResolvesSecondTrack()
    {
        var tracks = Tracks(TrackKind.Video, TrackKind.Audio);
        // y in [56, 112) is the second lane.
        var hit = Hit(x: 0, y: TrackHeight + 1, tracks);
        Assert.That(hit.Track, Is.SameAs(tracks[1]));
    }

    [Test]
    public void HitTest_NegativeY_IsMiss()
    {
        var tracks = Tracks(TrackKind.Video);
        Assert.That(Hit(x: 0, y: -1, tracks), Is.EqualTo(TimelineHit.Miss));
    }

    [Test]
    public void HitTest_PastLastLane_IsMiss()
    {
        var tracks = Tracks(TrackKind.Video, TrackKind.Audio);
        // Two lanes occupy [0, 112); y beyond that is empty space below the stack.
        var hit = Hit(x: 0, y: TrackHeight * 2 + 5, tracks);
        Assert.That(hit, Is.EqualTo(TimelineHit.Miss));
    }

    // ---- X → frame ----------------------------------------------------------

    [Test]
    public void HitTest_FrameIsRoundedFromX()
    {
        var tracks = Tracks(TrackKind.Video);
        // x = 5px at 6px/frame ≈ 0.83 → rounds to frame 1.
        Assert.That(Hit(x: 5, y: 10, tracks).Frame, Is.EqualTo(1));
        // x = 60px → frame 10 exactly.
        Assert.That(Hit(x: 60, y: 10, tracks).Frame, Is.EqualTo(10));
    }

    // ---- clip body vs empty -------------------------------------------------

    [Test]
    public void HitTest_OverClipBody_ReturnsBody()
    {
        var tracks = Tracks(TrackKind.Video);
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 2)); // [10, 70)
        // Frame 40 (x = 240px) is mid-clip, well away from either edge band.
        var hit = Hit(x: 40 * Ppf, y: 10, tracks);
        Assert.That(hit.Zone, Is.EqualTo(HitZone.Body));
        Assert.That(hit.Clip, Is.SameAs(tracks[0].Clips[0]));
    }

    [Test]
    public void HitTest_EmptyLaneSpace_ReturnsNoneAndNoClip()
    {
        var tracks = Tracks(TrackKind.Video);
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 2)); // [10, 70)
        // Frame 5 (x = 30px) is before the clip — empty space.
        var hit = Hit(x: 5 * Ppf, y: 10, tracks);
        Assert.That(hit.Zone, Is.EqualTo(HitZone.None));
        Assert.That(hit.Clip, Is.Null);
        Assert.That(hit.Track, Is.SameAs(tracks[0]));
    }

    [Test]
    public void HitTest_PastClipEnd_IsHalfOpen()
    {
        var tracks = Tracks(TrackKind.Video);
        tracks[0].Clips.Add(MediaClip(timelineStart: 0, sourceSeconds: 1)); // [0, 30)
        // Frame 30 is the first frame past the clip (half-open) → empty.
        var hit = Hit(x: 30 * Ppf, y: 10, tracks);
        Assert.That(hit.Clip, Is.Null);
    }

    // ---- edge zones ---------------------------------------------------------

    [Test]
    public void HitTest_NearLeftEdge_ReturnsLeftEdge()
    {
        var tracks = Tracks(TrackKind.Video);
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 2)); // left px = 60
        // 2px right of the left edge → within the 5px band.
        var hit = Hit(x: 60 + 2, y: 10, tracks);
        Assert.That(hit.Zone, Is.EqualTo(HitZone.LeftEdge));
    }

    [Test]
    public void HitTest_NearRightEdge_ReturnsRightEdge()
    {
        var tracks = Tracks(TrackKind.Video);
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 2)); // right px = 70*6 = 420
        var hit = Hit(x: 420 - 2, y: 10, tracks);
        Assert.That(hit.Zone, Is.EqualTo(HitZone.RightEdge));
    }

    [Test]
    public void HitTest_TinyClip_EdgeBandClampedToHalfWidth_LeavesBodyGrabbable()
    {
        var tracks = Tracks(TrackKind.Video);
        // A 1-frame clip at 6px/frame is 6px wide. Band clamps to 3px each side, so the
        // exact center (x = leftPx + 3) is neither edge — it must resolve to Body so a
        // narrow clip can still be grabbed for Move.
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 1.0 / Framerate)); // 1 frame
        var leftPx = 10 * Ppf;
        var hit = Hit(x: leftPx + 3, y: 10, tracks);
        Assert.That(hit.Zone, Is.EqualTo(HitZone.Body));
    }

    // ---- locked tracks are inert (ADR 0006) ---------------------------------

    [Test]
    public void HitTest_LockedTrack_ReportsTrackAndFrameButNoClip()
    {
        var tracks = Tracks(TrackKind.Video);
        tracks[0].Locked = true;
        tracks[0].Clips.Add(MediaClip(timelineStart: 10, sourceSeconds: 2));
        var hit = Hit(x: 40 * Ppf, y: 10, tracks);
        Assert.That(hit.Track, Is.SameAs(tracks[0]), "track still resolves (scrub/clear-select works)");
        Assert.That(hit.Clip, Is.Null, "no clip reported on a locked track");
        Assert.That(hit.Zone, Is.EqualTo(HitZone.None));
        Assert.That(hit.Frame, Is.EqualTo(40));
    }

    // ---- degenerate inputs --------------------------------------------------

    [Test]
    public void HitTest_NonPositivePixelsPerFrame_IsMiss()
    {
        var tracks = Tracks(TrackKind.Video);
        Assert.That(TimelineHitTester.HitTest(10, 10, tracks, 0, TrackHeight), Is.EqualTo(TimelineHit.Miss));
    }

    [Test]
    public void HitTest_NullTracks_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TimelineHitTester.HitTest(0, 0, null!, Ppf, TrackHeight));
    }

    // ---- helpers ------------------------------------------------------------

    private static TimelineHit Hit(double x, double y, IReadOnlyList<Track> tracks) =>
        TimelineHitTester.HitTest(x, y, tracks, Ppf, TrackHeight, EdgePx);

    private static List<Track> Tracks(params TrackKind[] kinds)
    {
        var list = new List<Track>();
        foreach (var kind in kinds)
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
