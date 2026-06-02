using System;
using System.Windows;
using System.Windows.Input;

namespace Flicksy.VideoEditor.Interaction;

/// <summary>
/// Dispatches timeline pointer events to a tool via three-tier resolution (ADR 0007):
/// <list type="number">
///   <item><description><b>Mid-gesture</b>: any tool with <see cref="ITimelineTool.IsActive"/>
///     keeps every move/up of the in-progress gesture, regardless of what's under the pointer
///     now.</description></item>
///   <item><description><b>Selected mode</b>: when a mode tool is engaged (Razor, via
///     <see cref="SelectedModeTool"/>) it wins the down event — cut-at-click is modal, not
///     hit-zone. Lands in #12 phase 5; the slot exists now so it drops in without reworking
///     dispatch.</description></item>
///   <item><description><b>Hit-zone</b>: otherwise the tool is picked by the down point's
///     <see cref="HitZone"/> (Body → Move, edges → Trim, None → Marquee) via the
///     <see cref="HitZoneSelector"/> the host supplies.</description></item>
/// </list>
/// This generalises Drawing's two-tier (active → selected) router by adding the point-aware
/// hit-zone fallback its no-arg selector can't express — the reason it isn't reused.
/// </summary>
public sealed class TimelineToolRouter
{
    private readonly Func<HitZone, ITimelineTool?> _hitZoneSelector;

    // The tool that received the down event; move/up route here for the gesture's lifetime
    // even though IsActive is the authoritative mid-gesture signal.
    private ITimelineTool? _capturedTool;

    /// <summary>
    /// <paramref name="hitZoneSelector"/> maps the down point's <see cref="HitZone"/> to the
    /// tool that should handle it. The host owns the mapping because it owns the tool instances.
    /// </summary>
    public TimelineToolRouter(Func<HitZone, ITimelineTool?> hitZoneSelector)
    {
        _hitZoneSelector = hitZoneSelector ?? throw new ArgumentNullException(nameof(hitZoneSelector));
    }

    /// <summary>
    /// The engaged mode tool (Razor), or null for hit-zone dispatch. Set when the user toggles
    /// Razor mode (#12 phase 5); wins the down event over hit-zone resolution.
    /// </summary>
    public ITimelineTool? SelectedModeTool { get; set; }

    /// <summary>True while the dispatched tool is mid-gesture — the host gates move/up on this.</summary>
    public bool HasActiveGesture => _capturedTool is { IsActive: true };

    public bool OnPointerDown(Point point, TimelineHit hit, MouseButtonEventArgs e)
    {
        var tool = SelectedModeTool ?? _hitZoneSelector(hit.Zone);
        _capturedTool = tool;
        return tool is not null && tool.OnPointerDown(point, hit, e);
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        _capturedTool?.OnPointerMove(point, e);
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        var tool = _capturedTool;
        _capturedTool = null;
        tool?.OnPointerUp(point, e);
    }

    /// <summary>
    /// Routes a no-button hover to the tool the point's zone would dispatch to (or the engaged
    /// mode tool), so edge-hover trim cursors appear without a click. An in-progress gesture is
    /// never in hover state by definition.
    /// </summary>
    public void OnPointerHover(Point point, TimelineHit hit, MouseEventArgs e)
    {
        var tool = SelectedModeTool ?? _hitZoneSelector(hit.Zone);
        tool?.OnPointerHover(point, hit, e);
    }
}
