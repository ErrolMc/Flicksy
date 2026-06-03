using System.Windows;
using System.Windows.Input;

namespace Flicksy.VideoEditor.Interaction;

/// <summary>
/// Host surface a timeline tool interacts with. Mirrors the shape of
/// <c>Flicksy.Drawing.Interaction.IDrawingSurface</c> (tool + router + surface) but shares no
/// code with it — Drawing's seam is bound to <c>DrawingItem</c>/<c>DrawingViewModel</c>, this
/// one to <see cref="Project.Clip"/>/<see cref="Project.Track"/> (ADR 0007). A single
/// timeline-wide surface (not per-lane) so cross-track move / marquee / snap all reason across
/// tracks from one tested coordinate map.
/// <para>
/// Tools depend only on this interface + the <see cref="ViewModels.TimelineViewModel"/>, never
/// on the WPF host control, so their gesture math unit-tests against a fake surface.
/// </para>
/// </summary>
public interface ITimelineSurface
{
    /// <summary>Zoom level: content pixels per timeline frame (the view's PixelsPerFrame).</summary>
    double PixelsPerFrame { get; }

    /// <summary>Per-lane vertical extent in pixels (lane stacking unit on the Y axis).</summary>
    double TrackHeight { get; }

    /// <summary>Cursor displayed by the surface. Setting <c>null</c> reverts to the default.</summary>
    Cursor? Cursor { get; set; }

    /// <summary>
    /// Resolves a content-space point (relative to the lanes host) to the track / clip / zone /
    /// frame under it. Returns <see cref="TimelineHit.Miss"/> off the lane stack.
    /// </summary>
    TimelineHit HitTest(Point contentPoint);

    /// <summary>
    /// Maps a raw pointer event to a content-space point relative to the lanes host (where Y=0
    /// is the top of the first lane and X=0 is frame 0, both pre-scroll-offset).
    /// </summary>
    Point GetContentPoint(MouseEventArgs e);

    /// <summary>Capture pointer input so the active tool keeps receiving move/up off-surface.</summary>
    void CapturePointer();

    /// <summary>Release a previously captured pointer.</summary>
    void ReleasePointer();

    /// <summary>
    /// Shows or updates the marquee rubber-band at <paramref name="contentRect"/> (content space,
    /// same space as <see cref="GetContentPoint"/>) on the timeline-wide lanes container's adorner
    /// layer — not a single lane, so the band spans tracks. Called on each move of a marquee drag.
    /// </summary>
    void ShowMarquee(Rect contentRect);

    /// <summary>Removes the marquee rubber-band. Safe to call when none is shown.</summary>
    void HideMarquee();
}
