# Video editor document model

## Decision

The video editor's document is a `Project` containing `ProjectSettings` and an ordered list of `Track`s. Each `Track` holds an ordered list of `Clip`s and an unordered list of `Transition`s.

Three concrete clip types:
- **`MediaClip`** — references a video/audio source file with in/out source points, plus per-clip transform, filter chain, volume, and speed.
- **`GraphicsClip`** — a time-bounded clip wrapping a single `DrawingItem` (shared with the snip editor) plus a transform; one graphic object per clip. (Originally a *container of `DrawingItem`s* — revised to one-object-per-clip by [ADR 0013](0013-graphicsclip-editing-one-object-per-clip.md).)
- **`Transition`** — *not* a clip. A relationship between two adjacent `MediaClip`s on the same track, stored on the track in a separate list keyed by clip pair.

All timeline positions and durations are **integer frame counts** at the project's framerate. Sub-frame audio is expressed in sample offsets, not fractional frames.

The document is **designed to be JSON-serializable from day one**, even though save/load won't ship in v1. No WPF types (`Brush`, `Geometry`, `MatrixTransform`) appear on the model surface — those live on derived properties or view-side wrappers.

## Why

- **Three clip types beat one fat clip.** `MediaClip`'s fields (source path, in/out, speed, volume) and `GraphicsClip`'s fields (items, item-tree edits) have almost no overlap. A discriminated union (abstract `Clip` + concrete subtypes) makes invalid states unrepresentable and keeps serialization tight.
- **Transitions as boundary objects beat transitions as clips.** A transition is not playable on its own — it is a blended region between two specific clips. Modeling it as a clip forces awkward invariants ("this clip must always have a clip immediately before and after"). As a boundary object, trim/move/split operations on `MediaClip`s update the transition list cleanly.
- **Integer frames beat float seconds.** Splitting a clip "at the playhead" must be exact. Float drift in `double` seconds means a split-then-rejoin is not the identity. Frame counts are exact, comparable, and serialize as integers.
- **Designed-for-serialization from day one beats retrofit.** Once the model has WPF types in it, save/load requires a parallel DTO layer plus a translation pass. Cheaper to keep UI types off the model surface from the start.

## Considered Options

- **Float seconds for timeline positions.** Rejected (drift, equality, exactness).
- **Transitions as clip-edge properties** (`Clip.LeftTransition` / `Clip.RightTransition`). Rejected — both clips would store the same transition, so one becomes the source of truth or they desync.
- **One `Clip` class with nullable fields per use case.** Rejected — type-system noise, validation noise, and the snip-editor's `DrawingItem` collection wants to live somewhere with no `SourcePath`.
- **`Project` framerate fixed at creation.** Rejected — users will want to change framerate after they realize they need 60fps for a clip. Lossy remap is acceptable; locking the framerate is not.

## Consequences

- **Changing project framerate is lossy.** Clip boundaries must be remapped to the new frame grid. Acceptable for v1.
- **Audio sub-frame events** (sample offsets) bypass the integer-frame grid. Two time units coexist: timeline frames for everything visible, sample offsets for audio fades / sample-accurate audio edits.
- **Transitions move when their adjacent clips move.** Trim, split, and clip-delete operations all need to keep the per-track transition list consistent. Likely a small helper on `Track` that does this in one place.
- **`MediaClip` source-time uses `TimeSpan` or microseconds, not source frames.** Source files have their own framerate; storing source time as a framerate-independent value (`TimeSpan`) is simpler than tracking per-source framerate.
- **Per-clip transform is a typed struct, not a `MatrixTransform`.** Fields: `Position`, `Scale`, `Rotation`, optional `CropRect`. The compositor builds an actual matrix at render time; the document doesn't store one.
