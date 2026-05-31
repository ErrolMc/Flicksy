# Timeline interaction layer

## Decision

#12's direct-manipulation gestures (select, move, trim, marquee, plus an optional Razor mode) are handled by a new interaction package in `Flicksy.VideoEditor/Interaction/` that **mirrors the shape of `Flicksy.Drawing/Interaction/` (tool + router + surface) but shares no code with it.** A single, timeline-wide `ITimelineSurface` + `TimelineToolRouter` handles all pointer input; clip rendering stays per-lane.

**Mirror the pattern, don't reuse the types.** New `ITimelineTool`, `ITimelineSurface`, `TimelineToolRouter` live in VideoEditor. Drawing's equivalents are bound to `DrawingItem` / `DrawingViewModel`; the timeline operates on `Clip` / `Track`. The reuse is conceptual (a gesture object owns its state and depends only on a host surface), not a shared base class.

**`ToolRouter` cannot be reused.** Its tool selector is a no-arg `Func<IDrawingTool?>` — it picks the tool from the *toolbar mode*, with no access to the pointer position. The timeline has no mode toolbar; it picks the tool by **hit-zone** at the down point (clip body to Move, edge to Trim, empty lane to Marquee), which needs point-aware dispatch the existing router can't express.

**Three-tier dispatch.** The router resolves a gesture to: mid-gesture tool (`IsActive`) then selected mode tool (Razor, when engaged) then hit-zone tool. This generalises Drawing's two-tier (`active` then `selected`) by adding the hit-zone fallback, and keeps a "selected mode" slot so Razor (cut at the click point, distinct from `S` = split selected at playhead) drops in without rework.

**One timeline-wide surface, not per-lane.** Cross-track move, cross-track marquee, and cross-track snap all reason across tracks. Per-lane surfaces fight this on two fronts: pointer capture pins a gesture to the lane it started in, and adorner clipping cuts the marquee / drag-ghost at that lane's bounds — and resolving "which track is the pointer over now" needs a timeline-wide Y-to-track map regardless. The single surface hit-tests from the model (Y to track, X to frame, clip + zone from `TimelineStart` / `Duration`) and attaches via **Preview** pointer events on the lanes container, the same mechanism `TimelineView` already uses for `PreviewMouseWheel` / `PreviewMouseLeftButtonDown` on `RootBorder`. Rendering is untouched — `ClipsLaneView` still draws its clips.

**Input migrates up; non-gestures stay.** Because the surface intercepts pointer-down first, click-to-select moves out of `ClipView` into the Move tool, and bin-drop handling consolidates onto the timeline-wide target. `ClipView` keeps its non-gesture concerns (context menu, inline rename).

## Why

- **Hit-zone dispatch is the interaction the feature actually describes** — dragging a body, an edge, or empty space mean different things by location, not by a selected mode. A mode toolbar would add modal state the issue never asked for.
- **Testability is the reason to abstract at all.** Tools depend only on `ITimelineSurface`, so move / trim / marquee / split logic unit-tests against a fake surface in `Flicksy.VideoEditor.Tests` — the same payoff Drawing's tools get, and the only structured way to cover the gesture math.
- **The cross-track hit-test math must exist either way.** A single surface gives "point to track / clip / zone" one tested home; per-lane just rebuilds it piecemeal behind capture / clipping workarounds.

## Considered Options

- **Code-behind handlers in `ClipsLaneView` / `ClipView`.** Simpler for ~3 gestures; rejected for unit-testability and because cross-track still needs a timeline-wide map.
- **Reuse Drawing's `ToolRouter`.** Rejected — its no-arg selector can't hit-zone-dispatch, and it's typed against `IDrawingTool` over `DrawingItem`.
- **Per-lane surfaces.** Rejected — capture + adorner-clipping fight every cross-track gesture, and a timeline-wide coordinate map is needed anyway.
- **Pure mode toolbar (Premiere-style palette).** Rejected — modal UI not wanted; the one selectable Razor mode is kept inside the three-tier router instead.

## Consequences

- **New folder `Flicksy.VideoEditor/Interaction/`**: `ITimelineTool`, `ITimelineSurface`, `TimelineToolRouter`, and the Move / Trim / Marquee / Razor tools.
- **#7's click-select and #9's bin-drop handling move into the interaction layer**; `ClipView` is reduced to rendering + context menu + rename.
- **Input bindings**: the scissor button = split selected at playhead; `C` = Razor mode; `S` / `Delete` / `Esc`-cancel handled in `VideoEditorWindow.OnPreviewKeyDown`, gated on `TextBoxBase` focus like `Space`.
- **Undo wiring** (same feature): an `UndoManager` instance on `VideoEditorViewModel` (mirroring PostSnip's `DrawingViewModel.History`), a new `Flicksy.VideoEditor/Undo/Commands/` holding `TrimClipCommand` / `SplitClipCommand` / `MoveClipCommand` / `RemoveClipCommand` / `MoveClipBetweenTracksCommand`, and a generalised `CompositeCommand` extracted in `Flicksy.Drawing/Undo` to drop its `DrawingViewModel` coupling so both surfaces can bundle multi-clip edits.
