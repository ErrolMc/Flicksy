using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Interaction;

/// <summary>
/// Result of resolving a timeline pointer position against the document. Carries the
/// <see cref="Track"/> under the pointer (null when the Y is past the last lane), the
/// <see cref="Clip"/> under it (null on empty lane space), the <see cref="Zone"/>
/// (body / edge / none), and the timeline <see cref="Frame"/> the X maps to. Produced by
/// <see cref="TimelineHitTester"/> and consumed by <see cref="ITimelineTool"/>s through the
/// <see cref="ITimelineSurface"/>.
/// </summary>
public readonly record struct TimelineHit(Track? Track, Clip? Clip, HitZone Zone, int Frame)
{
    /// <summary>A miss: pointer is off the lane stack (past the last track or above the first).</summary>
    public static readonly TimelineHit Miss = new(null, null, HitZone.None, 0);
}
