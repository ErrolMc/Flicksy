# Flicksy.Drawing — shared rendering library

Detailed map for **Flicksy.Drawing**, the library both interactive editors build on: the `DrawingItem` hierarchy, the tool/interaction system, the undo manager, FFmpeg playback, and the `DrawingView` canvas. References Icons; referenced by [snip-editor.md](snip-editor.md) (PostSnip) and [video-editor.md](video-editor.md) (which reuses its undo/composite stack, `UserSettingsStore`, and `ViewLocator`). Part of the [architecture index](../ARCHITECTURE.md). Section numbers are local to this file. Keep it updated per [CLAUDE.md](../../CLAUDE.md).

## 1. Layout

```
Flicksy.Drawing/
├── Source/        ← DrawingItem hierarchy + ShapeKind
├── Interaction/   ← Tool/router/config
├── Undo/          ← UndoManager + shared commands
├── Media/         ← IVideoPlayer + FFmpeg
├── Settings/      ← UserSettingsStore (writable JSON user prefs, %LOCALAPPDATA%\Flicksy)
├── Controllers/   ← TextEditingController
├── Controls/
│   DrawingView/   ← The canvas
│   SelectionOverlayView
│   VideoSurface
│   TextEditingHost.cs  ← attached property
├── Helpers/
│   BitmapExtensions
│   DrawingMath
└── ViewModels/
    DrawingViewModel
    SelectionOverlayViewModel
```

Icon PNGs (rotate puck, shape options, toolbar buttons) live in **Flicksy.Icons** — see the [architecture index §6](../ARCHITECTURE.md).

## 2. ViewModels

| ViewModel | Owns / Coordinates |
| --- | --- |
| [DrawingViewModel](../../Flicksy.Drawing/ViewModels/DrawingViewModel.cs) | `ObservableCollection<DrawingItem> Items` (z-ordered), `SelectedItem`, `EditingTextItem`, `History` (UndoManager). All gesture transitions (`BeginPenStroke`/`End...`, `BeginShape`/`End...`, `BeginText`/`BeginEditText`/`EndEditText`, `BeginTextStyleEdit`/`End...`). Layer move + delete commands. |
| [SelectionOverlayViewModel](../../Flicksy.Drawing/ViewModels/SelectionOverlayViewModel.cs) | `SelectedItem` + `IsActive` + `ShowHandles` + cached `CanonicalBounds`. Subscribes to the item's `Geometry`/`Transform.Changed` so the overlay redraws when the item moves. |

## 3. Drawing model (Flicksy.Drawing/Source)

All items inherit [DrawingItem](../../Flicksy.Drawing/Source/DrawingItem.cs) which provides `Geometry`, `MatrixTransform Transform`, abstract `CanonicalBounds`/`HitTest(localPoint)`/`Render(DrawingContext)`, and `Translate/Scale/RotateFrom(baseMatrix, ...)` helpers.

| Item | Geometry | Notes |
| --- | --- | --- |
| [PenStrokeItem](../../Flicksy.Drawing/Source/PenStrokeItem.cs) | Catmull-Rom-style smoothed `PathGeometry` over a `PointCollection`. | Brush + thickness immutable per stroke. Bounds inflated by thickness/2. |
| [ShapeItem](../../Flicksy.Drawing/Source/ShapeItem.cs) | `Square` (Rect), `Circle` (Ellipse), `Line` (LineGeometry), `Arrow` (PathGeometry: shaft + filled arrowhead triangle). | `EffectiveFill`/`EffectiveStroke` exposed for the XAML data template — arrow's "fill" is its outline brush so the head fills solidly. `IsDegenerate` predicate suppresses commit on tap-without-drag. |
| [TextItem](../../Flicksy.Drawing/Source/TextItem.cs) | `FormattedText.BuildGeometry(origin)`. | Properties are mutable via `SetText`/`SetFontFamily`/`SetFontSize`/`SetFill`/`SetOutline`. Geometry rebuilds on every mutation. `IsEditing` flag tracks the in-place editor. |

## 4. Interaction system (Flicksy.Drawing/Interaction)

Decouples gesture handlers from the WPF host so the canvas (or a future video-editor canvas) can swap in tools without changing them.

- [IDrawingSurface](../../Flicksy.Drawing/Interaction/IDrawingSurface.cs) — host capabilities: dimensions, content scale (zoom), cursor set/get, pointer capture, `TryGetCanvasPoint(MouseEventArgs, ...)`.
- [IDrawingTool](../../Flicksy.Drawing/Interaction/IDrawingTool.cs) — gesture interface: `OnPointerDown/Move/Up/Hover` + `IsActive` flag for in-progress gestures.
- [ToolRouter](../../Flicksy.Drawing/Interaction/ToolRouter.cs) — dispatches pointer events. Prefers any tool with `IsActive == true` over the currently-selected tool, so a gesture that started under tool A still receives Move/Up after the user toggles to tool B.
- [InputSmoothing](../../Flicksy.Drawing/Interaction/InputSmoothing.cs) — single-pole EMA for pen jitter.
- [Config/IPenConfig](../../Flicksy.Drawing/Interaction/Config/IPenConfig.cs), [IShapeConfig](../../Flicksy.Drawing/Interaction/Config/IShapeConfig.cs), [ITextConfig](../../Flicksy.Drawing/Interaction/Config/ITextConfig.cs) — per-tool settings exposed by the host (`DrawingView` implements all three from its dependency properties).

Tools (`Flicksy.Drawing/Interaction/Tools`), used by the snip editor's toolbar:

| Tool | Gesture | Pushes undo on |
| --- | --- | --- |
| [SelectTool](../../Flicksy.Drawing/Interaction/Tools/SelectTool.cs) | Click to select, drag inside bounds to move, drag corner to scale (anchor = opposite corner). Double-click TextItem → open editor. Hover sets resize cursors per corner+rotation. | `TransformCommand` on pointer up (only if matrix changed). |
| [PenTool](../../Flicksy.Drawing/Interaction/Tools/PenTool.cs) | Down begins stroke (seeds smoother). Move appends smoothed points gated by `max(1.5, thickness/2)` minimum distance. | `AddItemCommand` (via `DrawingViewModel.EndPenStroke`). |
| [ShapeTool](../../Flicksy.Drawing/Interaction/Tools/ShapeTool.cs) | Drag to size. **Shift** = constrain (square/circle: equal sides; line/arrow: 45° snap). | `AddItemCommand` (via `EndShape`). Degenerate shapes are removed without an undo entry. |
| [EraseTool](../../Flicksy.Drawing/Interaction/Tools/EraseTool.cs) | Down + drag deletes whichever item is topmost under the pointer. | One `RemoveItemCommand` per delete, bundled into a `CompositeCommand` on pointer up if >1. |
| [TextTool](../../Flicksy.Drawing/Interaction/Tools/TextTool.cs) | Click on TextItem → `BeginEditText`. Click on empty → `BeginText` + `BeginEditText`. No drag. | `AddItemCommand` or `TextEditCommand` pushed by `DrawingViewModel.EndEditText`. |

Rotation lives on the **SelectionOverlayView** (not a tool) because it interacts with the puck handle drawn outside the item's bounds — see [SelectionOverlayView.OnRotateHandleMouseDown](../../Flicksy.Drawing/Controls/SelectionOverlay/SelectionOverlayView.xaml.cs). It pushes its own `TransformCommand` analogously to SelectTool's scale/translate.

Crop is **not** an `IDrawingTool` — it edits image-level state rather than the drawing collection. Selecting the Crop toolbar button drives [CropOverlayViewModel](../../Flicksy.PostSnip/ViewModels/CropOverlayViewModel.cs) via [PostSnipViewModel](../../Flicksy.PostSnip/ViewModels/PostSnipViewModel.cs)'s tool-change handler (BeginEdit on enter, CommitEdit on leave). All gesture handling (resize / move / draw new) lives in [CropOverlayView.xaml.cs](../../Flicksy.PostSnip/Controls/CropOverlayView.xaml.cs). New rects are clamped to the original image bounds.

## 5. Undo (Flicksy.Drawing/Undo + Flicksy.PostSnip/Undo/Commands/CropCommand)

[UndoManager](../../Flicksy.Drawing/Undo/UndoManager.cs): two stacks, capped at 100 entries. Exposes `UndoCommand`/`RedoCommand` RelayCommands. `Push` clears redo and trims oldest.

Convention: commands are pushed **after** the change has already mutated state (gestures mutate live for visual feedback). `Redo()` is therefore only invoked when stepping forward through the redo stack, never on the initial push.

| Command | When |
| --- | --- |
| [AddItemCommand](../../Flicksy.Drawing/Undo/Commands/AddItemCommand.cs) | New item committed (pen stroke end, shape end, text commit on new item). |
| [RemoveItemCommand](../../Flicksy.Drawing/Undo/Commands/RemoveItemCommand.cs) | Single delete (Delete key, single-tap erase). |
| [CompositeCommand](../../Flicksy.Drawing/Undo/Commands/CompositeCommand.cs) | Multi-step bundle (drag-erase that removed several items; video-editor multi-clip edits). Surface-agnostic — preserves selection via an optional [ICompositeSelectionScope](../../Flicksy.Drawing/Undo/ICompositeSelectionScope.cs) ([DrawingSelectionScope](../../Flicksy.Drawing/Undo/Commands/DrawingSelectionScope.cs) for the snip editor), or `null` for none. |
| [TransformCommand](../../Flicksy.Drawing/Undo/Commands/TransformCommand.cs) | Move/scale/rotate gesture end. Snapshots before/after `Matrix`. |
| [MoveLayerCommand](../../Flicksy.Drawing/Undo/Commands/MoveLayerCommand.cs) | Layer up/down toolbar buttons. |
| [TextEditCommand](../../Flicksy.Drawing/Undo/Commands/TextEditCommand.cs) | Existing TextItem's text changed in place. |
| [TextStyleCommand](../../Flicksy.Drawing/Undo/Commands/TextStyleCommand.cs) | Batch of font/size/fill/outline changes from the Text settings popup (captured on open, pushed on close). Uses `TextStyleSnapshot`. |
| [CropCommand](../../Flicksy.PostSnip/Undo/Commands/CropCommand.cs) | Crop committed (push at `CropOverlayViewModel.CommitEdit` if before/after differ). Undo/redo call `ApplyCommittedCrop`. |

## 6. Media (Flicksy.Drawing/Media)

| File | Purpose |
| --- | --- |
| [IVideoPlayer](../../Flicksy.Drawing/Media/IVideoPlayer.cs) | **Push-shaped** playback abstraction (one source, internal clock, decode-ahead queue, event-driven frame delivery): `Open/Play/Pause/Seek/Close`, `FrameReady`/`PositionChanged`/`StateChanged`/`MediaEnded` events. Used by PostSnip. |
| [FFmpegVideoPlayer](../../Flicksy.Drawing/Media/FFmpegVideoPlayer.cs) | `IVideoPlayer` impl. Decodes ahead into a `BlockingCollection<VideoFrame>` (capacity 6) on a Task; presents on `CompositionTarget.Rendering` ticks. `_seekLock` is **`TryEnter`** in the render path so a background scrub seek can't stall the UI for tens of ms. Uses `ArrayPool<byte>` for frame buffers — every code path that takes a frame is responsible for returning the buffer. |
| [IMediaDecoder](../../Flicksy.Drawing/Media/IMediaDecoder.cs) | **Pull-shaped** decoder abstraction (sync seek + grab, no clock, no queue): `GetVideoFrameAt(time)`, `GetAudioSamplesAt(time, Span<float>)`, plus stream availability + duration + video dimensions. Consumed by the compositor (#10), which holds one decoder per `Clip.Id`. Audio output is interleaved stereo float32 at the target sample rate configured at construction — the decoder owns all resampling/remixing. PostSnip's eventual migration onto this primitive is tracked in #23. |
| [FFmpegMediaDecoder](../../Flicksy.Drawing/Media/FFmpegMediaDecoder.cs) | `IMediaDecoder` impl backed by FFMediaToolkit. Video reads via `MediaFile.Video.GetFrame(t)` (same inline-seek path `FFmpegVideoPlayer.DoSeek` uses) and rents the frame buffer from `ArrayPool<byte>`; FFMediaToolkit's `GetFrame` reads forward cheaply for sequential/repeat frames and seeks only on a jump, so it is the video "read cursor" (no explicit one is kept). A `Lock` serializes calls because `MediaFile` is not thread-safe. **Audio decode is real** (#11): an audio read cursor reads source frames forward (`TryGetNextFrame`) during playback and seeks (`GetFrame` + head-skip to `time`) only on a discontinuity; leftover source samples + linear-resampler interpolation state persist across calls for click-free output (`_audioCursorTime` ± half-call tolerance decides continuous-vs-seek). Remix: mono→duplicate, stereo→passthrough, >2→front-two; rate conversion is linear interp (bit-exact passthrough when source rate == target rate). **Decode downscale (ADR 0008)**: an optional `targetVideoSize` ctor arg sets `MediaOptions.TargetVideoSize`, so swscale emits pre-shrunk frames for reduced-quality preview (cuts the BGRA convert/copy, not codec decode). |
| [HardwareMediaDecoder](../../Flicksy.Drawing/Media/HardwareMediaDecoder.cs) | Hardware `IMediaDecoder` (video-only, `HasAudio=false`) on raw FFmpeg.AutoGen. Per-instance device, **CUDA-first then d3d11va**: same NVDEC silicon on NVIDIA, but d3d11va's readback Map stalls the GPU pipeline (~8–13 ms/frame at 1080p) where CUDA's cuMemcpy is ~1 ms; d3d11va covers AMD/Intel. GPU frames are read back (`av_hwframe_transfer_data`, NV12/P010 self-described) and one swscale pass converts to BGRA32 **and** applies the ADR 0008 decode scale. Mirrors `FFmpegMediaDecoder` cursor semantics (500 ms forward-read threshold, backward keyframe seek + decode-discard, silent-null failures) and caches the last readback frame for repeat requests. Construction throws when the source can't decode on the GPU — static device probe, `avcodec_get_hw_config` gate, then a first-frame probe, tried per device type — so the cache falls back to software per source. `IsAvailable` folds in the `Disabled` kill switch, set once at startup from the video editor's user settings (`UseHardwareDecode` in [VideoEditorSettings](../../Flicksy.VideoEditor/Services/VideoEditorSettings.cs)). See [ADR 0010](../adr/0010-hardware-video-decode.md). |
| [FfmpegLocator](../../Flicksy.Drawing/Media/FfmpegLocator.cs) | One-time `Initialize()` at app startup; sets `FFMediaToolkit.FFmpegLoader.FFmpegPath` **and** `FFmpeg.AutoGen.ffmpeg.RootPath` (direct AutoGen callers must bind even when they're the process's first FFmpeg touch). See the [architecture index §1.2](../ARCHITECTURE.md) for probe order. |
| [VideoFrame](../../Flicksy.Drawing/Media/VideoFrame.cs) | Plain struct: `Buffer`, `BufferLength`, `Width`, `Height`, `Stride`, `Pts`. |
| [PlaybackState](../../Flicksy.Drawing/Media/PlaybackState.cs) | `Idle`/`Loading`/`Paused`/`Playing`/`Ended`. |

## 7. Controls

| Control | Notes |
| --- | --- |
| [DrawingView](../../Flicksy.Drawing/Controls/DrawingView/DrawingView.xaml) + [.xaml.cs](../../Flicksy.Drawing/Controls/DrawingView/DrawingView.xaml.cs) + [.DependencyProperties.cs](../../Flicksy.Drawing/Controls/DrawingView/DrawingView.DependencyProperties.cs) | The canvas. Renders all items via DataTemplates (PenStrokeItem/ShapeItem/TextItem → WPF `Path`). Implements `IDrawingSurface`/`IPenConfig`/`IShapeConfig`/`ITextConfig` and wires a `ToolRouter`. Rebuilds tool instances when its `DataContext` (the `DrawingViewModel`) changes. Hosts the in-place text editor TextBox in `EditOverlayCanvas`, managed by [TextEditingController](../../Flicksy.Drawing/Controllers/TextEditingController.cs). |
| [SelectionOverlayView](../../Flicksy.Drawing/Controls/SelectionOverlay/SelectionOverlayView.xaml.cs) | Corner handles + rotate puck. Projects item canonical bounds through `item.Transform` and the host's `ContentToViewport` transform (so handles stay screen-sized regardless of zoom). Owns the rotate gesture. |
| [VideoSurface](../../Flicksy.Drawing/Controls/VideoSurface.xaml.cs) | Subscribes to `IVideoPlayer.FrameReady`. Writes BGRA32 pixels into a `WriteableBitmap` sized to the video's first frame. |

## 8. Where to look for common changes

| Change request | Primary file(s) |
| --- | --- |
| Add a new drawing item type | new class in [Source/](../../Flicksy.Drawing/Source) inheriting `DrawingItem`, DataTemplate in [DrawingView.xaml](../../Flicksy.Drawing/Controls/DrawingView/DrawingView.xaml). |
| Add a new undoable action | new `IUndoableCommand` in [Drawing/Undo/Commands/](../../Flicksy.Drawing/Undo/Commands) (shared) or [PostSnip/Undo/Commands/](../../Flicksy.PostSnip/Undo/Commands) (snip-specific like crop), push from the call site **after** mutation. |
| Modify video playback (engine) | [FFmpegVideoPlayer](../../Flicksy.Drawing/Media/FFmpegVideoPlayer.cs) implements [IVideoPlayer](../../Flicksy.Drawing/Media/IVideoPlayer.cs) (push-shaped, internal clock + decode-ahead). The PostSnip transport UI that drives it lives in [snip-editor.md](snip-editor.md). |
