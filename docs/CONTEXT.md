# Flicksy

A Windows desktop tool with two surfaces, each its own process:

- **Snip editor** — fast capture-and-annotate flow. One image or video in, drawings on top, save out. Lives in `Flicksy.PostSnip.exe` (existing `PostSnipWindow`, the project formerly called `Flicksy.Editor`).
- **Video editor** — non-linear video editor (NLE) for assembling clips on a timeline. Lives in `Flicksy.VideoEditor.exe`, sibling to `Flicksy.PostSnip.exe`.

Both processes share rendering primitives via the `Flicksy.Drawing` class library: drawing items, tools, undo plumbing, and FFmpeg playback primitives. Each process has its own document, its own undo stack, and its own ffmpeg init. They communicate only by file paths and CLI args, like the existing Agent → Snipper → Editor chain.

## Language

### Surfaces

**Snip**:
A one-shot annotation session over a single captured image or video. Lives in `PostSnipWindow`. No time axis on the document — drawings are timeless overlays painted on top of the captured media.
_Avoid_: "snipping tool" (overloaded with the Windows utility), "annotation".

**Video editor**:
The NLE surface for assembling multiple clips into a finished video. Lives in `VideoEditorWindow`. Document is a timeline.
_Avoid_: "movie editor", "video maker".

**Shell overlay**:
The video editor's in-window modal layer: a dim backdrop over the whole editor window with one centered tile (Project Settings, Settings, future Export). One at a time, shown through the root VM's `OverlayHost`. Distinct from the `Overlay` *track kind* — say "shell overlay" (or name the specific tile) when the track kind could be in scope.
_Avoid_: "dialog window" / "popup" (no OS window is involved — it is a layer inside the editor window).

### Video editor document

**Project**:
The root document of a video editing session. Contains `ProjectSettings`, an ordered list of `Track`s, and (eventually) is serializable to a `.flicksy` JSON file. While unsaved, lives at a temp-folder path.
_Avoid_: "movie", "composition", "sequence".

**ProjectSettings**:
Mutable project-level settings: `Framerate`, `Resolution` (the canonical composite canvas), `AudioSampleRate`. `Resolution` can be changed mid-project.
_Avoid_: "project config".

**Editor settings (user preferences)**:
App-wide, user-editable preferences that persist across sessions and across projects (e.g. "show performance stats", GPU vs CPU decode). Distinct from `ProjectSettings`: editor settings are the same regardless of which `Project` is open, and live outside the document. Surfaced in the Settings shell-overlay tile (the gear), separate from the Project Settings tile.
_Avoid_: conflating with `ProjectSettings` (those are per-document); "options", "config".

**Track**:
An ordered horizontal lane on the timeline that contains `Clip`s. Has a `TrackKind` (`Video` / `Audio` / `Overlay`) used by the UI to constrain which clips can be dropped. The compositor does not branch on `TrackKind` — it branches on clip type. Carries three flags — `Muted`, `Locked`, `Disabled` — defined below.
_Avoid_: "layer", "row", "channel".

**Muted (track flag)**:
Audio-only mute. An `Audio` track with `Muted=true` contributes zero samples to the audio mix. Video output is unaffected. The M button is shown only on Audio track headers (hidden on Video/Overlay) because Video-track audio control is done by splitting the clip first. To silence a `Streams=Both` clip's audio, use the per-clip **Detach audio** command to produce an Audio track, then mute that.
_Avoid_: applying "mute" to video (use `Disabled` for video skip).

**Locked (track flag)**:
The editor refuses edits to clips on a locked track (move, trim, split, delete are all no-ops). Compositor is unaffected. The L button is shown on all track kinds.
_Avoid_: "frozen", "read-only" (Locked is the canonical term here, matching Premiere/Resolve).

**Disabled (track flag)**:
Track is fully skipped by the compositor — no video output, no audio contribution (including any `Streams=Both` clip's audio). The track row stays at full height in the timeline UI but renders with a ghost effect (reduced opacity + desaturation) so it's always clickable to re-enable in place. The D button is shown on all track kinds.
_Avoid_: "hidden" (the row is not hidden; it is ghosted), "invisible".

**Clip**:
The unit of content on a `Track`, occupying a half-open interval `[TimelineStart, TimelineStart+Duration)` in project time. Abstract base; concrete types are `MediaClip` and `GraphicsClip`. Clips on the same track do not overlap.
_Avoid_: "segment", "block", "item" (collides with `DrawingItem`).

**MediaClip**:
A `Clip` that references a `MediaSource` by id. Holds `MediaSourceId`, `SourceIn`, `SourceOut` (in source time), `TimelineStart`, `Speed`, `Streams` (`Video` / `Audio` / `Both`, default `Both`), per-clip `Transform` (position/scale/rotate/crop), `FilterChain`, `Volume`. Timeline duration is `(SourceOut - SourceIn) / Speed`. The underlying file path lives on the `MediaSource`, not the clip — many clips can share one source. `Streams` controls what the compositor renders: dropping a video+audio source on a Video track creates `Streams=Both`; the per-clip **Detach audio** command flips a `Streams=Both` clip to `Streams=Video` and adds a paired `Streams=Audio` clip on a freshly-created Audio track named `"<source video track> (Audio)"` (with `" N"` de-collision suffix if the name is already in use). Each detach creates its own new track. Video tracks accept `Streams ∈ {Both, Video}` in any mix; Audio tracks accept `Streams=Audio` only.
_Avoid_: "video clip" (it can be audio-only), "source clip".

**MediaSource**:
A first-class entity in `Project.MediaSources` representing an imported video/audio file the project knows about. Probed at import via `FFMediaToolkit`. Fields: `Id`, `SourcePath`, `DisplayName` (defaults to filename, user-renameable), `Duration`, `HasVideo`, `HasAudio`, video-only `Width`/`Height`/`SourceFramerate`, audio-only `SampleRate`/`ChannelCount`, and a runtime `IsMissing` flag. The Media bin in the UI is a view over `Project.MediaSources`. `MediaClip`s reference a `MediaSource` by `Id`, so relocating one missing file fixes every clip that used it.
_Avoid_: "asset", "media item", "import", "bin entry" (the bin is just the UI for the list).

**GraphicsClip**:
A `Clip` that holds a list of `DrawingItem`s (the existing pen/shape/text items) visible only between `TimelineStart` and `TimelineStart+Duration`. Has a `Transform` like `MediaClip`. The Snip editor's drawings live in a flat list of `DrawingItem`s; the Video editor's drawings live inside a `GraphicsClip`.
_Avoid_: "overlay clip" (overlay is a *track kind*, not a clip kind), "text clip" (it's more general).

**Transition**:
A blended boundary between two adjacent `MediaClip`s on the same track. Properties: `LeftClip`, `RightClip`, `Type` (crossfade, fade-to-black, wipe, ...), `Duration`. **Not a `Clip`** — it is a relationship between two clips, stored on the track in a separate list.
_Avoid_: "fade", "blend", "transition clip".

**TimelineTime / SourceTime**:
Two distinct time domains. `TimelineTime` is position in the assembled project; `SourceTime` is position inside a source file. A `MediaClip` maps a `[SourceIn, SourceOut]` window in `SourceTime` to a `[TimelineStart, TimelineStart+Duration]` window in `TimelineTime`, possibly scaled by `Speed`.
_Avoid_: using bare "time" when which one matters.

**Frame**:
The canonical unit of `TimelineTime`. All clip positions and durations are integer frame counts at the project's `Framerate`. Sub-frame audio offsets use samples, not fractional frames.
_Avoid_: "second" or `double`-seconds as a storage unit.

### Timeline editing operations

**Split**:
Cutting one `Clip` into two adjacent clips at the playhead's `TimelineTime`. For a `MediaClip` the source range divides at the mapped `SourceTime`; the two halves inherit the original's other properties.
_Avoid_: "cut", "slice", "razor" (the razor is the gesture; the operation is a Split).

**Trim**:
Dragging a `Clip`'s left or right edge to change its in/out point and `Duration` — for a `MediaClip`, adjusting `SourceIn` (left edge) or `SourceOut` (right edge).
_Avoid_: "resize", "crop" (crop is the snip editor's image operation).

**Move**:
Dragging a `Clip` to a new `TimelineStart`, optionally onto another `Track` of the same `TrackKind`.
_Avoid_: "reposition", bare "drag" (that's the gesture, not the result).

**Detach audio**:
The per-`MediaClip` operation that moves a `Streams=Both` clip's audio onto a new `Audio` track, leaving the original as `Streams=Video`; the two clips are unlinked afterward.
_Avoid_: "Split audio" (collides with **Split**), "extract audio", "unlink".

### Video editor rendering

**PlaybackEngine**:
The video editor's playback driver. Owns the playback clock and, while playing, advances `TimelineTime` at the project `Framerate`, driving the `Compositor` to present each frame and the `AudioMixer` to feed the audio output device. The video-editor counterpart to `IVideoPlayer`'s clock role, but driving the multi-clip `Compositor` / `AudioMixer` instead of a single source. The Transport bar's play/pause/seek/step delegate to it; it writes the playhead position back for the UI to observe.
_Avoid_: "player" (reserved for `IVideoPlayer`'s push shape), "transport" (the UI bar and its view-model, not the engine).

**Compositor**:
The video render engine that produces a composited frame at a given `TimelineTime`, gathering inputs from all `Track`s. Pure-ish input/output: `(Project, frame) → frame image`. Owns an internal video-decoder cache keyed by `Clip.Id`. Implementation choice (`SkiaCompositor` today, `Direct2DCompositor`/`Dx11Compositor` in the future) is encapsulated behind an `ICompositor` seam. The audio half is the separate `AudioMixer`. See [ADR 0004](adr/0004-compositor-design.md).
_Avoid_: "renderer" (overloaded with `DrawingItem.Render`), "mixer" (that's the `AudioMixer`).

**AudioMixer**:
The audio counterpart to the `Compositor`: produces the audio mix for a given `TimelineTime` by summing each audible clip's samples scaled by `Volume`. Pure-ish input/output: `(Project, frame) → audio buffer`. Owns its own audio-decoder cache, separate from the `Compositor`'s video cache, so a `Streams=Both` clip's audio and video advance on independent decoder cursors. Lives behind an `IAudioMixer` seam; driven by the `PlaybackEngine` on the audio output thread.
_Avoid_: folding it back into "compositor" (the compositor is video-only).

**CompositionLayer**:
The unit of work a `Compositor` consumes per frame — one entry per visible `Clip` at `TimelineTime` T, carrying its z-order, source-time mapping (speed-aware), and `Transform2D`. Produced by `CompositionPlanner`. Backend-agnostic; every `ICompositor` implementation walks the same layer list and only the paint step varies.
_Avoid_: "render layer" (no concept of nested layers in the model).

**Composited frame**:
The compositor's per-frame visual output — the pixels painted into the caller-owned `WriteableBitmap` at project resolution, or a smaller target at reduced **preview quality** (not a distinct type; the caller owns and reuses one bitmap, see [ADR 0004](adr/0004-compositor-design.md) and [ADR 0008](adr/0008-proxy-mode-preview-rendering.md)). Backend-neutral — GPU implementations produce the same surface via readback.
_Avoid_: "rendered frame" (collides with raw decoded frames from a `MediaDecoder`).

**Preview quality (playback resolution)**:
A view-only setting that renders the **preview** at a fraction of project resolution (`Full` / `½` / `¼` / `⅛`) for cheaper playback and scrubbing — on the fly, like Premiere's "Playback Resolution". The `Compositor` scales the project-space layer stack into a smaller target bitmap **and** decodes each video source at the matching `TargetVideoSize`; all per-clip transforms and crops stay in project coordinates, and the preview's `Stretch=Uniform` scales the result back to full display size. Transient per editor window (lives on `PreviewViewModel`, not the serialized `Project`); never affects export. See [ADR 0008](adr/0008-proxy-mode-preview-rendering.md).
_Avoid_: "proxy" / "proxy mode" for this (reserve "proxy media" for the separate, unbuilt pre-transcoded-file workflow), "low-quality mode", "draft mode"; applying it to export (export is always full resolution).

**Media decoder**:
Per-source primitive that produces raw video frames or audio samples from a single source file at a given `SourceTime`. Pull-shaped (synchronous `GetVideoFrameAt(t)` / `GetAudioSamplesAt(t, n)`), no playback clock. Distinct from `IVideoPlayer`, which is push-shaped and owns its own clock for the snip editor's video playback. Lives in `Flicksy.Drawing/Media/` so it's available to both surfaces; the eventual unification of PostSnip's playback onto `IMediaDecoder` is tracked in issue #23. The video editor's video seams prefer the hardware implementation (`HardwareMediaDecoder`; CUDA on NVIDIA, d3d11va otherwise) per source, silently falling back to the software decoder; audio always decodes in software (ADR 0010).
_Avoid_: bare "decoder" without "media" prefix, "player" (that's `IVideoPlayer`'s shape).

**Decode-ahead pump (prefetch pump)**:
The background worker that decodes upcoming video frames ahead of the playhead during playback so the UI thread only composites already-decoded frames (`VideoPrefetchPump`). A producer thread fills a bounded, generation-stamped queue of **frame bundles** — each one output frame's decoded video layers plus its `CompositionLayer` snapshot; `PreviewViewModel` consumes them on the UI thread and `PlaybackEngine` owns the pump's lifecycle. Moves only the **decode** off the UI thread; compositing stays on it. The producer decodes strictly forward; if it trails the playhead by more than a resync threshold (~1 s) it jumps forward to the playhead in one seek (**bounded resync**) so video drops frames with bounded lag rather than drifting behind the audio. On a true miss (nothing decoded up to the playhead yet) the consumer holds the previous frame. See [ADR 0009](adr/0009-decode-ahead-pump.md).
_Avoid_: "render-ahead" (only decode is off-thread, not compositing), "proxy" (that's **preview quality** / proxy media), "buffer" or "frame queue" for the **frame bundle** (a bundle is all of one frame's layers together, not a single image).

### Shared primitives (snip + video editor)

**DrawingItem**:
A visual primitive (`PenStrokeItem`, `ShapeItem`, `TextItem`) with `Geometry`, a `MatrixTransform`, hit-testing, and a `Render(DrawingContext)` method. Used by both surfaces:
- In the snip editor: lives directly in `DrawingViewModel.Items`, always visible.
- In the video editor: lives inside a `GraphicsClip`, visible only during the clip's time window.
_Avoid_: "shape" (collides with `ShapeItem`), "annotation", "overlay element".

**Drawing tool**:
A handler implementing `IDrawingTool` (`PenTool`, `ShapeTool`, `TextTool`, `SelectTool`, `EraseTool`) that converts pointer gestures into `DrawingItem` mutations. Used by both surfaces unchanged — the tools don't know whether the items they emit will end up in a flat list or a `GraphicsClip`.
_Avoid_: "brush" (means stroke color/fill in WPF).

## Flagged ambiguities

**"Overlay"** is overloaded:
- A `TrackKind.Overlay` is a track that the UI lets you put `GraphicsClip`s on.
- A `GraphicsClip` is not called an "overlay clip" — that's a track-kind property leaking into the clip name.
- The snip editor's `SelectionOverlayView` / `CropOverlayView` are *UI overlays* in WPF terms, unrelated to the video editor concept.
Disambiguate with `TrackKind.Overlay` or "UI overlay" rather than bare "overlay".

**"Item"**:
- `DrawingItem` is a visual primitive (pen stroke, shape, text).
- A `Clip` is *not* an item; never call it one. `DrawingViewModel.Items` is the snip editor's flat drawing list — in the video editor the equivalent name is `Project.Tracks[k].Clips`.

## Example dialogue

> **Dev:** Where should the per-clip volume control live in the UI?
> **Domain:** It's a property of the `MediaClip` — only `MediaClip`s have audio. `GraphicsClip`s and `Transition`s don't.
> **Dev:** What about the overall mix?
> **Domain:** That's not a project-level setting; it's just every `MediaClip`'s `Volume`. You can mute a whole track in the UI but the model truth is per-clip.
> **Dev:** And when the user splits a clip at the playhead?
> **Domain:** You're splitting at a `TimelineTime`, which is an integer `Frame`. Convert that to the clip's `SourceTime` via the speed mapping, then create two `MediaClip`s sharing the original `SourcePath` — one with the original `SourceIn` and the new in-between `SourceOut`, the other with the in-between point as its new `SourceIn` and the original `SourceOut`. Any `Transition` attached to the original on its right edge moves to the right new clip.
