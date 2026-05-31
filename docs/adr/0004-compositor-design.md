# Compositor design

> **Amended by [ADR 0005](0005-playback-threading-and-audio.md):** #11 moves `RenderAudio` out of `ICompositor` into a separate `IAudioMixer`, and resolves the playback threading model this ADR defers to #11 (v1 is UI-thread; off-thread decode-ahead is phase 2). The decisions below stand except where 0005 supersedes them.

## Decision

The video editor's compositor is a stateful object that produces a composited frame and an audio buffer for a given `(Project, frame)` input. Its responsibilities, dependencies, and boundaries are:

**API surface.** `ICompositor` lives in `Flicksy.VideoEditor/Composition/`. Two independent calls:

```csharp
public interface ICompositor : IDisposable
{
    void RenderFrame(Project project, int frame, WriteableBitmap target);
    AudioBuffer RenderAudio(Project project, int frame);
}
```

_Amended by ADR 0005: `RenderAudio` has moved to a separate `IAudioMixer`; `ICompositor` retains only `RenderFrame`._

`RenderFrame` paints into a caller-owned `WriteableBitmap` sized to `ProjectSettings.{ResolutionWidth, ResolutionHeight}`; the caller reuses one bitmap across frames so the compositor allocates no per-frame surface. The bitmap is left unfrozen (it's reused), so the compositor and the consumer that presents it share one thread (the UI thread today). `AudioBuffer.Samples` is `float[]` interleaved stereo at `ProjectSettings.AudioSampleRate`, one video frame's worth of samples per call (`SampleRate / Framerate` samples per channel).

**Compute backend.** SkiaSharp. CPU backend in scaffolding, GPU backend deferred to a follow-up issue when filters/transitions demand it. The choice is encapsulated inside `SkiaCompositor`; the `ICompositor` seam is backend-neutral so future `Direct2DCompositor` / `Dx11Compositor` implementations can replace it without touching callers.

**Backend-agnostic planning extracted.** `CompositionPlanner` is a static class with `PlanFrame(project, frame) → IReadOnlyList<CompositionLayer>`. Walks `Project.Tracks` (skipping `Disabled` tracks), finds clips active at frame `T`, returns an ordered layer list with each layer's z-order, source-time mapping (speed-aware), and `Transform2D`. Zero Skia dependency. Every backend reuses it; only paint differs.

**Decoder ownership.** The compositor owns an internal `Dictionary<Guid, IMediaDecoder>` keyed by `Clip.Id` (not `MediaSourceId` — two clips of the same source at different `SourceTime`s each need their own decoder cursor). Decoders are opened lazily on first reference to a clip, evicted when a clip becomes inactive or under memory pressure. Cache is internal — callers see only `RenderFrame` / `RenderAudio`.

**Decoder primitive.** New `IMediaDecoder` interface in `Flicksy.Drawing/Media/`, parallel to the existing `IVideoPlayer` (which is playback-shaped — push events, internal clock). `IMediaDecoder` is pull-shaped — synchronous `GetVideoFrameAt(TimeSpan)` and `GetAudioSamplesAt(TimeSpan, sampleCount)`. Concrete `FFmpegMediaDecoder` wraps `FFMediaToolkit.MediaFile`. One primitive per source file handles both video and audio streams.

`IVideoPlayer` stays as-is; PostSnip continues to use it. A follow-up issue (#23) tracks PostSnip's eventual migration onto `IMediaDecoder` + a thin playback clock.

**Render resolution.** Always at `ProjectSettings.{ResolutionWidth, ResolutionHeight}`. Preview surface scales via WPF's `Stretch=Uniform`. Export uses the same path. Proxy-mode (lower-resolution preview rendering) is deferred to its own issue.

**Track flags introduced by this work.** Three new fields on `Track`:

| Flag | Shown on | Compositor effect | Editor effect |
| --- | --- | --- | --- |
| `Muted` | Audio tracks only (UI hides the M button on Video/Overlay) | Audio track contributes zero to the mix | None |
| `Locked` | All tracks | None | `#12`'s edit commands refuse to operate on clips in this track |
| `Disabled` | All tracks | Track fully skipped — no video, no audio (including `Streams=Both` audio on a Video track) | None |

A disabled track's row stays at full height in the timeline but renders with a ghost effect (reduced opacity + desaturation) so it can always be re-enabled in place.

**Threading.** `RenderFrame` / `RenderAudio` are synchronous and single-call-in-flight (no internal locking). `RenderFrame` paints into the caller's reusable, unfrozen `WriteableBitmap`, so the compositor and whoever presents that bitmap must share one thread (the UI thread today); the `GraphicsClip` path independently needs a Dispatcher for the same reason. The real threading pipeline (decode-ahead, scrub coalescing, audio chunking, and any cross-thread buffer hand-off such as ping-pong bitmaps) is owned by #11.

**Scope of this slice.** Design + one working `RenderFrame` path. In scope:

- `MediaClip` composite with `Streams ∈ {Video, Both}` (decode → `Transform2D` → composite at z-order)
- `MediaClip` audio mix with `Streams ∈ {Audio, Both}` (decode samples → `Volume` scale → sum)
- `GraphicsClip` rendering (`DrawingItem`s painted through `Transform2D`)
- Z-order across tracks (Overlay above Video)
- Speed-aware `TimelineTime → SourceTime` mapping
- `Track.{Muted, Locked, Disabled}` model fields + UI bindings on `TrackHeaderView`
- Preview-driven demo: `PreviewViewModel` owns a reusable `WriteableBitmap`, subscribes to `TransportViewModel.Playhead`, and calls `RenderFrame` on the UI thread to repaint it in place; `PreviewView`'s `<Image>` binds to it via `CurrentFrame`

Out of scope (each is its own issue):

- Transitions (#14)
- Per-clip filters (#16)
- Per-clip opacity / blend modes
- Per-clip audio fades (#18)
- Real-time playback loop (#11)
- Export pipeline (#20)
- Proxy-mode rendering
- GPU backend swap

## Why

- **Compositor as `(Project, frame) → frame` matches the way #11, export, scrubbing, and thumbnail-strip rendering all want to consume it.** A pure-ish input/output makes the compositor reusable across surfaces. Threading and decode-ahead are caller concerns, not compositor concerns.

- **Internal decoder cache beats caller-supplied decoders.** The compositor has to walk active clips at every frame anyway — that walk produces exactly the keys the cache needs. Pushing the cache up to the caller duplicates that walk.

- **Cache keyed by `Clip.Id`, not `MediaSourceId`.** Two clips of the same source at different `SourceTime`s need two decoder cursors. A `MediaSourceId`-keyed cache forces seek-juggling on every render. `Clip.Id` is the correct grain.

- **New `IMediaDecoder` beats reusing `IVideoPlayer`.** `IVideoPlayer` is push-shaped (events, internal clock, decode-ahead queue) — designed for one-source-with-its-own-playhead. The compositor needs pull-shaped, sync-seek, N-decoder-simultaneously semantics. Fighting the existing API would obscure the design. The two primitives can share an internal `MediaFile` wrapper without sharing public shape.

- **Compositor lives in `Flicksy.VideoEditor`, not `Flicksy.Drawing`.** PostSnip never composites — its drawings overlay a single image/video via the WPF visual tree. Putting Skia (and its 6MB native dependency) in `Flicksy.Drawing` would force PostSnip to carry weight it never uses. The decoder primitive *does* belong in Drawing because PostSnip will use it (post-#23).

- **SkiaSharp over Direct2D/DX11 for the first backend.** SkiaSharp has a managed wrapper, well-documented API, identical CPU/GPU surface (swap the surface, not the code), and WPF integration via `SKElement` / `WriteableBitmap`. Direct2D requires SharpDX/Vortice plumbing for similar capability; custom DX11 is months of work. SkiaSharp lets the scaffolding pass produce working pixels on day one and admits a GPU swap when filters/transitions demand it.

- **`CompositionPlanner` extracted now, not later.** ~50 lines of pure logic. Forces the "Skia is one of many backends" claim to be real rather than aspirational. Tests cleanly (no Skia, no decoder, no WPF — just timeline math). Bugs in `Speed` mapping or active-clip detection surface in planner tests before they surface as wrong pixels.

- **Always render at project resolution.** One render path serves preview, scrub, export, thumbnails. Per-clip transforms, filter parameters, and crop rectangles are all sized against project resolution — rendering at preview-surface size would force resolution-dependent scaling of every parameter. Proxy mode (a smaller render target for perf) is a real concept but belongs in its own issue when measurements demand it.

- **Mute is audio-only and Audio-tracks-only.** Matches Premiere/FCP convention. A user who wants to silence a `Streams=Both` clip's audio splits it first (issue #12 / already-landed split-audio operation), producing a real Audio track that can be muted. Adding video-track mute would introduce overlap with Disabled and force per-kind UI variation for no real workflow gain.

- **Disabled keeps the track row visible (ghosted), never hidden.** UI that disappears without an obvious path back is a discoverability hazard. A ghosted full-height row stays clickable and re-enableable in place.

## Considered Options

### Compute backend

- **Pure CPU via `WriteableBitmap` + System.Drawing/WPF.** Rejected. Software-blit of full-frame BGRA layers at 30fps is ~250 MB/s of memcpy on the UI thread even before filters; it tops out at 1-2 clips before perceptible stutter at 1080p. SkiaSharp's CPU backend uses SIMD-accelerated paint and clears the same workload faster, with the GPU upgrade path already wired through the same API.
- **Direct2D via SharpDX or Vortice.** Rejected. Windows-native and fast, but the API surface for compositing-with-transforms-and-blending is broader than what we need, and the integration code for WPF + bitmap interop is more than SkiaSharp's `SKElement`. No managed wrapper for the geometry/path primitives the GraphicsClip path needs.
- **Custom DX11/12 via Silk.NET / Vortice raw.** Rejected. Maximum control, months of plumbing. Not a starting point for a v1 NLE.

### Decoder primitive

- **Use `IVideoPlayer` directly.** Rejected. `IVideoPlayer` is push-shaped — `Open`/`Play`/`Pause`/`Seek` + `FrameReady` events + an internal clock + a `BlockingCollection<VideoFrame>` decode-ahead queue. The compositor wants synchronous pull (`GetFrameAt(t)`) for N decoders simultaneously. Wiring it would mean adapting an event-driven, clock-bearing API into a pull surface — fighting the type.
- **Replace `IVideoPlayer` entirely with `IMediaDecoder` + a clock layer.** Rejected for #10. PostSnip's playback already works; refactoring it here adds risk for no functional gain. Tracked separately as #23.

### Compositor location

- **In `Flicksy.Drawing`.** Rejected. Forces SkiaSharp into PostSnip transitively (PostSnip references Drawing). PostSnip never composites — its drawings paint via WPF DataTemplates onto an `Image`. Dragging a 6MB native dependency through PostSnip for no consumer is structural waste.
- **In a new `Flicksy.Composition` library.** Rejected. The compositor has no consumers outside the video editor. Single-consumer libraries are overhead without benefit.

### Decoder cache key

- **`MediaSourceId`.** Rejected. Fails when two clips reference the same source at different `SourceTime`s — a single decoder is a stateful cursor in one file and can't be at two positions at once. Forces per-frame seek-juggling.
- **`(MediaSourceId, ActiveClipId)` tuple.** Rejected as cosmetic — `Clip.Id` is already globally unique within the project, so the source id adds nothing. Carry it in the cache *value* (the decoder knows its source), not the key.

### `ICompositor` API shape

- **Caller-supplied `SKCanvas`.** Rejected. Couples every caller to SkiaSharp at the public seam, defeating the backend-swap goal. Callers would need a Skia-shaped canvas argument that a future `Direct2DCompositor` couldn't supply.
- **`Render(project, frame) → (frame, AudioBuffer)` single call.** Rejected. Bundles unrelated lifecycles — playback wants audio in larger chunks than one video frame's worth for smooth output; scrubbing wants frames without audio; export wants both per-frame. Two calls let each caller compose what it needs.
- **Compositor allocates + freezes a fresh `WriteableBitmap` per `RenderFrame`.** Rejected on the perf pass. A frozen bitmap is trivially cross-thread safe, but a fresh ~8 MB allocation per frame at 1080p is ~240 MB/s of GC pressure under sustained playback. The caller now owns and reuses one bitmap (`RenderFrame(project, frame, target)`); the trade is that a reused bitmap can't be frozen, so presentation is same-thread — acceptable because the `GraphicsClip` path already pins `RenderFrame` to the UI thread, and #11 owns any cross-thread (ping-pong) story.

### Mute semantics

- **Mute present on every track, affects all output (audio + video) for that track.** Rejected. Collides with Disabled — both mean "produce nothing from this track" with no clear distinction.
- **Mute present on every track, audio-only effect (silences `Streams=Both` clips' audio contributions on Video tracks too).** Rejected. Workflow gain is small (the same effect is one click away via Split audio + mute the audio track) and the UI clutter of a non-Audio "Mute" button is real. Premiere and FCP don't show Mute on Video tracks for the same reason.

### Disabled UI

- **Track row collapses to 0 height.** Rejected. UI that disappears without an in-place affordance creates a discoverability hazard. Users hit D accidentally and lose the track.
- **Track row collapses to a slim ~16px row.** Considered. Cleaner than full-height ghost but loses visual context for what's *on* the disabled track. Rejected in favor of full-height ghost, which keeps the track's clip layout visible.

### `CompositionPlanner` extraction

- **Inline in `SkiaCompositor`, extract when a second backend lands.** Rejected. Cheap to do now (~50 lines), proves the abstraction is real, gives a pure test surface for the timeline math (which is otherwise tangled with paint).

## Consequences

- **New folder `Flicksy.VideoEditor/Composition/`** contains `ICompositor`, `SkiaCompositor`, `CompositionPlanner`, `CompositionLayer`, `AudioBuffer`. SkiaSharp dependency added to `Flicksy.VideoEditor.csproj` (`SkiaSharp` only — the compositor renders offscreen into a `WriteableBitmap`, so `SkiaSharp.Views.WPF`/`SKElement` hosting is not needed).

- **New files in `Flicksy.Drawing/Media/`**: `IMediaDecoder.cs`, `FFmpegMediaDecoder.cs`. `IVideoPlayer` and `FFmpegVideoPlayer` are untouched.

- **`Track` gains three observable fields** (`Muted`, `Locked`, `Disabled`). All three serialize to JSON. `TrackHeaderView`'s existing stub `ToggleButton`s bind to them; the M button gets a `Visibility` converter on `Track.Kind == TrackKind.Audio`. `ClipsLaneView` and the track row container apply opacity/desaturation when `Track.Disabled` is true.

- **`PreviewViewModel` gains a `CurrentFrame` property** (an `ImageSource` backed by a `WriteableBitmap` it owns and reuses across frames, reallocated only on a resolution change) and subscribes to `TransportViewModel.Playhead` changes. `PreviewView.xaml` swaps its placeholder `DrawingImage` for `<Image Source="{Binding CurrentFrame}" Stretch="Uniform"/>`.

- **`MediaClip.Duration`'s speed-mapping is consumed for the first time.** Bugs in `(SourceOut - SourceIn) / Speed * Framerate` math will surface as wrong-frame composites. `CompositionPlanner` unit tests should cover speed mapping explicitly.

- **`Track.Disabled` is checked in two places** in the compositor: the video pass (`if (track.Disabled) continue;`) and the audio mix pass. `Track.Muted` is checked only in the audio mix pass, and only for `TrackKind.Audio`.

- **Issue #23 (PostSnip → `IMediaDecoder`) is blocked by this issue.** Once `IMediaDecoder` ships, PostSnip's playback layer can be re-pointed at it.

- **Issue #11 inherits the threading pipeline.** A comment on #11 spells out the contract: `RenderFrame` is sync and single-call-in-flight; the playback engine in #11 owns decode-ahead, scrub coalescing, and audio chunking.

- **GPU backend swap is a future issue, not a future refactor.** The `ICompositor` seam means a new `Direct2DCompositor` or `Dx11Compositor` is additive; callers don't change.
