using System.Windows;
using System.Windows.Input;

namespace Flicksy.VideoEditor.Interaction;

/// <summary>
/// A self-contained pointer-driven timeline interaction (move, trim, marquee, razor). Tools own
/// their gesture state and depend only on <see cref="ITimelineSurface"/> +
/// <see cref="ViewModels.TimelineViewModel"/> — never on the WPF host. Mirrors
/// <c>Flicksy.Drawing.Interaction.IDrawingTool</c> in shape (see ADR 0007) but is typed against
/// the timeline's <see cref="Project.Clip"/>/<see cref="Project.Track"/> model.
/// <para>
/// Down handlers receive the resolved <see cref="TimelineHit"/> alongside the point, so a tool
/// the router dispatched by hit-zone doesn't re-run the hit-test. Move/Up only get the point
/// (the gesture already captured what it needs on down).
/// </para>
/// </summary>
public interface ITimelineTool
{
    /// <summary>
    /// <c>true</c> while a gesture is in progress (between <see cref="OnPointerDown"/> and
    /// <see cref="OnPointerUp"/>). The router keeps dispatching move/up to whichever tool is
    /// mid-gesture, regardless of hit-zone under the moving pointer.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Primary-button press. <paramref name="hit"/> is the surface's resolution of
    /// <paramref name="point"/>. Return <c>true</c> if the tool consumed the event.
    /// </summary>
    bool OnPointerDown(Point point, TimelineHit hit, MouseButtonEventArgs e);

    /// <summary>Pointer move while the primary button is held.</summary>
    void OnPointerMove(Point point, MouseEventArgs e);

    /// <summary>Primary-button release.</summary>
    void OnPointerUp(Point point, MouseButtonEventArgs e);

    /// <summary>Pointer move while no button is pressed (cursor affordances, e.g. trim edges).</summary>
    void OnPointerHover(Point point, TimelineHit hit, MouseEventArgs e);
}
