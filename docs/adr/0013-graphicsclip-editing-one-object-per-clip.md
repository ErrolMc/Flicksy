# GraphicsClip editing — one object per clip

## Decision

Each graphic object (one shape or one text element) is its **own** `GraphicsClip` wrapping a **single** `DrawingItem` — the Clipchamp / Final Cut model, *not* Premiere's one-clip-many-layers. This **supersedes [ADR 0002](0002-video-editor-document-model.md)'s** "`GraphicsClip` is a time-bounded container of `DrawingItem`s." Objects visible at the same instant occupy **separate Overlay tracks**; the editor auto-stacks Overlay tracks as objects are added.

Editing happens on a `DrawingView` overlay over the Preview, reusing `Flicksy.Drawing`'s tools **one object at a time**: the overlay's `DrawingViewModel` wraps the single selected/created clip's item collection plus the editor's shared `UndoManager`. Undo is **clip-level**.

## Why

- **Per-object timeline editing is the core value and the NLE standard** (Clipchamp, Final Cut, Resolve's Edit page). The original multi-item container forced every object in a clip to share one time window; independent timing/trim/move/split per object is what users expect.
- **Reuse over re-implement.** The objects *are* `DrawingItem`s, so the snip editor's tools / `DrawingView` / `TransformCommand` apply directly — unlike the timeline interaction layer ([ADR 0007](0007-timeline-interaction-layer.md)), which mirrors-but-shares-nothing precisely because its items are `Clip`s, not `DrawingItem`s.

## Key decisions

- **Coordinate space.** The overlay is hosted at **project resolution** and Uniform-scaled onto the composited frame's letterbox rect (mirrors PostSnip's native-pixels + ancestor-`ScaleTransform`). Item geometry is captured in project pixels — exactly what the compositor's `PaintGraphicsClip` already assumes.
- **One object at a time (v1).** The overlay edits the one selected/created object; other objects active at the playhead show via the composite backdrop. The aggregate "click any object in the preview" model is deferred.
- **Targeting.** Arming a drawing tool + the playhead drives it. Placing a new object creates a clip `[playhead, playhead + 3s]` on the top Overlay track if it's free across the window, else on a **new Overlay track inserted at the top** (newest-on-top z-order; user re-stacks via Move track up/down). Auto-select after place.
- **Tool set.** `SelectTool` / `ShapeTool` / `TextTool` only; `PenTool` (freehand) and `EraseTool` are snip-only (per-object clips make per-stroke clips impractical). Delete = `Delete` key.
- **Suppression.** The edited clip is dropped from the **preview composite** (preview-local layer filter; planner / compositor / export untouched) while its session is open — the overlay is the sole live renderer of that object, avoiding a stale double-draw.
- **Undo is clip-level.** Placing an object pushes a clip-add (plus an auto-created track, when one was made) bundled with the tool's item push into a **single** undo step via a new `UndoManager` batch primitive (`Begin`/`Commit` → `CompositeCommand`). Move / trim / delete reuse the timeline commands.
- **Trim.** Graphics-clip trim is a pure time-window edit (no `SourceIn`/`SourceOut`/`Speed`); clamps are neighbour clips / frame 0 / 1-frame minimum.
- **Split.** Both clip types split. A graphics split clones the wrapped `DrawingItem` into the right half (new `DrawingItem.Clone()`); no transitions, no source mapping.

## Consequences

- **[ADR 0002](0002-video-editor-document-model.md)'s `GraphicsClip` bullet is revised** — it holds one `DrawingItem`, not a list. The other 0002 decisions (three clip types, transitions-as-boundaries, integer frames, serialize-from-day-one) stand.
- **Right-rail shape restyle depends on [ADR 0012](0012-restylable-shape-items.md)** (built in PostSnip first). Text restyle reuses `TextStyleCommand` directly.
- **New additions to shared `Flicksy.Drawing`:** an additive `DrawingViewModel(ObservableCollection<DrawingItem>, UndoManager)` ctor; an `UndoManager` batch primitive; `DrawingItem.Clone()`.
- **Auto-stacking** can grow the Overlay-track count to the max number of simultaneous objects — accepted, and matches Clipchamp layers / Final Cut lanes.
- The numeric per-clip transform inspector stays in #15; #13's right rail is style-only.
