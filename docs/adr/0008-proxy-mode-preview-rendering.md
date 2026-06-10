# Preview quality: reduced-resolution preview rendering

## Status

Accepted. Implements the proxy-mode preview rendering [ADR 0004](0004-compositor-design.md) deferred as
"a real concept but belongs in its own issue when measurements demand it", and amends its render-resolution
rule: "always render at project resolution" still holds for export and the canonical path; the preview is
the documented exception. Later ADRs carry the decode-scale contract forward —
[ADR 0009](0009-decode-ahead-pump.md)'s pump decodes at the preview scale (and re-primes when it changes);
[ADR 0010](0010-hardware-video-decode.md)'s hardware decoder applies the same scale in its readback
conversion.

## Context

Playback v1 ([ADR 0005](0005-playback-threading-and-audio.md)) runs the whole video path on the UI thread:
each tick drives `PreviewViewModel.Render` → `SkiaCompositor.RenderFrame` → per-clip
`FFmpegMediaDecoder.GetVideoFrameAt`. The per-frame cost is the codec decode plus the post-decode steps —
swscale's BGRA convert, the copy into the rented buffer, and the Skia raster of the full-size frame — and
the post-decode share scales with pixel count, so playback and scrubbing are heavy at 1080p. ADR 0004 fixed
the render resolution at `ProjectSettings.{ResolutionWidth, ResolutionHeight}` because per-clip transforms,
crops, and future filter parameters are all sized against project resolution; rendering at preview-surface
size would force resolution-dependent rescaling of every parameter. Mature NLEs expose exactly this lever
as an on-the-fly playback-resolution control (Premiere's "Playback Resolution") — distinct from **proxy
media** (pre-transcoded stand-in files), which this is not.

## Decision

A view-only **preview quality** setting — `Full` / `½` / `¼` / `⅛` of project resolution per axis — that
renders the preview into a proportionally smaller target bitmap **and** decodes each video source at the
matching reduced size, for cheaper playback and scrubbing. It never affects export.

- **View state, not document state.** A `PreviewQuality` enum (`Full`/`Half`/`Quarter`/`Eighth`, per-axis
  scale 1 / 0.5 / 0.25 / 0.125 — ¼ down to 1/64 of the pixels) lives on `PreviewViewModel.SelectedQuality`:
  transient per editor window, never serialized into the `Project`, not undoable. Surfaced as an overlay
  dropdown ("Full", "1/2", "1/4", "1/8") in the preview's corner.
- **The target's size is the whole API.** ADR 0004's caller-owned reusable `WriteableBitmap` already flows
  into `RenderFrame`; the preview now sizes it at project resolution × the quality scale (`EnsureTarget`,
  recreating only when quality or project resolution changes), and the compositor derives the render scale
  from target ÷ project. `ICompositor.RenderFrame` gains no parameter — a full-resolution target renders
  1:1, byte-identical to the canonical path.
- **One canvas pre-scale; layers stay in project space.** `SkiaCompositor` applies a single
  `canvas.Scale(targetW / projectW, targetH / projectH)` before painting; every per-layer matrix and crop
  concats on top of it, still reasoning in project pixels. The preview `Image`'s existing `Stretch=Uniform`
  scales the smaller bitmap back to full display size, so a lower quality reads as the same picture at
  reduced fidelity.
- **Decode scale = render scale.** The compositor passes the same factor into its decoder cache:
  `MediaDecoderCache.GetOrCreate(clip, sampleRate, decodeScale)` opens each decoder at
  `TargetVideoSize = round(native × scale)` (clamped to ≥ 2 px per axis; native when scale ≥ 1 or source
  dims are unknown) and **re-opens** a clip's decoder when the scale changes, reusing it otherwise.
  `FFmpegMediaDecoder` takes an optional `targetVideoSize` ctor arg mapped to
  `MediaOptions.TargetVideoSize`, so swscale emits pre-shrunk BGRA frames; `VideoWidth`/`VideoHeight` keep
  reporting native dimensions.
- **Transforms map against the native source extent.** `PaintMediaClip` builds each layer matrix from the
  source's native dimensions (`clip.Source.Width/Height`, falling back to the frame's own size when
  unknown) and scales the possibly-smaller decoded frame onto it, keeping transform/crop math
  decode-size-independent.
- **Terminology.** The concept is "preview quality (playback resolution)". "Proxy" is reserved for proxy
  media — the separate, unbuilt pre-transcoded-file workflow; this setting transcodes nothing.

## Why

- **Downscale the decode too, not just the paint.** A smaller canvas alone leaves the heavy per-frame work
  untouched — the full-size swscale convert, full-size frame copy, and a downscale-at-raster. Setting
  `TargetVideoSize` moves the shrink into swscale's existing post-decode pass, so convert, copy, and raster
  all drop quadratically with quality. Accepted limitation: swscale runs after the codec, so the codec
  decode itself is unchanged (the residual cost ADR 0009 later moves off the UI thread).
- **Scale derived from target size over an explicit parameter.** The caller-owned-bitmap contract
  (ADR 0004) already makes the target's size an input, so no `ICompositor` signature change and zero risk
  to existing callers — export and the static path pass a full-resolution bitmap and are untouched. The
  seam stays backend-neutral.
- **Canvas pre-scale over re-deriving parameters at preview size.** ADR 0004 rejected preview-sized
  rendering because it "would force resolution-dependent scaling of every parameter". The pre-scale
  dissolves that objection instead of fighting it: transforms, crops, and filter parameters stay sized
  against project resolution; only the physical surface shrinks.
- **Re-open on scale change over per-frame rescaling.** `TargetVideoSize` is fixed when the `MediaFile`
  opens. A quality switch is a rare, user-driven event, so paying one re-open per active clip at the
  moment of the switch beats decoding native and rescaling at raster every frame, which would forfeit the
  convert/copy savings.
- **View-only over a project setting.** Quality expresses how this window views the project, like zoom —
  serializing it would let a perf knob mutate the document and surprise an export. Matches Premiere's
  Playback Resolution semantics.

## Consequences

- New `PreviewQuality` (+ `Scale()` extension + `PreviewQualityOption`) in
  `Flicksy.VideoEditor/ViewModels/`; `PreviewViewModel` gains `SelectedQuality`/`QualityOptions` and
  resizes its reusable target in `EnsureTarget`; `PreviewView` gains the overlay quality dropdown (the
  app's first `ComboBox`, dark-themed via a view-scoped style).
- `FFmpegMediaDecoder` gains the optional `targetVideoSize` ctor arg; `MediaDecoderCache.GetOrCreate`
  gains `decodeScale` and stores `(decoder, scale)` per clip — a quality switch costs one file re-open per
  active clip on its next render.
- Video-only: the `AudioMixer`'s decoder cache opens at scale 1; the audio mix is unaffected.
- Export is unaffected by construction — it passes a full-resolution target and no quality input exists
  anywhere in that path. ADR 0004's "always render at project resolution" survives as the export/canonical
  rule with the preview as the documented exception.
- The codec decode still runs full-cost on the UI thread, so preview quality mitigates decode-bound jank
  rather than fixing it; ADR 0009 moves the decode off-thread (decoding at this preview scale, re-priming
  its queue on a quality change) and ADR 0010 attacks the codec cost itself. Any future `IMediaDecoder` or
  decode seam must honor the target-size contract.
- CONTEXT.md gains the "Preview quality (playback resolution)" glossary entry, reserving "proxy" for proxy
  media.
