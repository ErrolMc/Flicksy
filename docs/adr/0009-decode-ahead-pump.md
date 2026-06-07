# Off-thread video decode-ahead for playback

## Status

Accepted. Implements the off-thread decode-ahead worker [ADR 0005](0005-playback-threading-and-audio.md)
deferred as "phase 2 … a worker [that] slots between the clock and `RenderFrame` … without changing the
seams."

## Context

Playback v1 (ADR 0005) ran the whole video path on the UI thread: the `CompositionTarget.Rendering`
tick writes `Playhead`, which synchronously drives `PreviewViewModel.Render` → `SkiaCompositor.RenderFrame`
→ the H.264 codec decode (`FFmpegMediaDecoder.GetVideoFrameAt`). The codec decode is the dominant per-frame
cost, so it blocks the dispatcher every frame — playback janks and the timeline/scrub feel heavy *while
playing*. Preview quality (ADR 0008) cut the post-decode steps (swscale convert + copy + raster) but not the
codec decode and not the thread it runs on.

## Decision

Move only the video **decode** off the UI thread; keep compositing there.

- **Decode-ahead, not render-ahead.** A background worker decodes upcoming frames; the UI tick still
  composites (so `GraphicsClip`'s `RenderTargetBitmap` Dispatcher requirement and the unfrozen
  `WriteableBitmap`'s thread affinity from ADR 0004 are untouched). The UI tick does only cheap Skia blits +
  present from already-decoded frames.
- **Compositor decode seam.** `IClipFrameProvider` (`Acquire`/`Release`) abstracts where a layer's frame
  comes from. `SkiaCompositor.RenderFrame` gains an optional `frames` provider (+ a `plannedLayers`
  snapshot); when null it uses a self-owned synchronous `DecodingFrameProvider` — the canonical
  export/scrub/static path, byte-for-byte unchanged. During playback the pump supplies both.
- **`VideoPrefetchPump`.** A producer thread fills a bounded queue of `FrameBundle`s (one output frame's
  decoded video layers + the planner snapshot). It owns its own `MediaDecoderCache`, separate from the
  compositor's synchronous one and the `AudioMixer`'s (ADR 0005's container-seek-thrash rule). The consumer
  (`PreviewViewModel`, UI thread) claims the **newest ready bundle at or before the playhead** with
  `BeginFrame`, renders, then `EndFrame`. The producer decodes **strictly forward** (cheap sequential reads);
  when it trails the playhead by more than a **resync threshold** (the engine passes one second of timeline
  frames) it jumps forward to the playhead in **one seek** — dropping the backlog to re-sync — then resumes
  forward decode. A true miss (nothing decoded up to the playhead yet) holds the previous frame. Together
  these degrade a slow decoder to dropped frames with **bounded** A/V lag, not a freeze and not an
  ever-growing drift — without best-effort consume, exact-frame matching against a sequential producer
  discards every frame the producer makes while it trails realtime, freezing playback.
- **Seek/scale = locked drain + generation bump** — *not* the audio path's lock-free `volatile pendingSeek`,
  which is only safe because audio's producer and consumer are the same (device) thread. The pump's producer
  and consumer are different threads sharing rented buffers, so a seek takes the lock, bumps a **generation**
  counter, and drains the queue returning every buffer; the producer only enqueues a bundle whose generation
  still matches, self-cancelling anything in flight. This guarantees no stale-epoch bundle is ever served.
- **Engine ownership.** `PlaybackEngine` owns the pump: `Start` on play, `Stop` on pause, `SeekTo` beside the
  existing audio seek, and `Dispose` that **joins the producer thread before disposing its decoder cache**.
  It points the preview at the pump via `IPlaybackFrameSink`.

## Why

- **Decode-ahead over render-ahead.** Compositing off-thread would need to pre-rasterize every `GraphicsClip`
  (its RTB needs a Dispatcher) and either reintroduce ADR 0004's per-frame ~8 MB allocation or add an
  ~8 MB/frame copy into the unfrozen bitmap on the UI thread — i.e. it only moves *part* of the pipeline off
  the UI thread. Decode is the expensive, parallelizable, non-WPF work; moving just it is additive to the
  existing seams, exactly as ADR 0005 predicted.
- **Drop-on-miss over block-until-ready.** Never stall the UI. It matches ADR 0005's system-clock-master
  sync: video is timed off the `Stopwatch`, so a slow frame is dropped (hold previous) while audio continues,
  and both resync on the next seek/pause. Blocking would freeze the dispatcher — the very thing we're fixing.
- **Bounded resync over per-frame catch-up.** A naive "always decode the current frame" makes the decoder
  jump every frame; each jump exceeds FFMediaToolkit's ~500 ms `VideoSeekThreshold`, forcing an
  `av_seek_frame` + decode-forward-to-keyframe *per frame*, which collapses long-GOP throughput (measured:
  1080p60 H.264 dropped from ~19 fps sequential to ~6 fps). Decoding strictly forward and resyncing only once
  per threshold window keeps the cheap sequential read path *and* caps A/V drift; the jump is large enough
  (> threshold) to actually skip work past the seek threshold. (Measured on a 1080p60 gameplay clip: ~17 fps
  displayed, max lag ~1.1 s; a clip the decoder keeps up with plays every frame at ~0 lag.)
- **Generation stamp + single lock over the audio `volatile` pattern.** With two threads sharing
  `ArrayPool` buffers, a seek must be an authoritative, locked drain; a lock-free flag would race the
  producer into a double-return or let a stale bundle (wrong size after a scale change, or wrong content
  after a timeline edit) reach the screen. One lock guarding `{queue, generation, nextFrame, scale, running}`
  — never held across a decode — plus the generation gate is sufficient and race-free.

## Consequences

- New seam `IClipFrameProvider` + `DecodingFrameProvider`; `ICompositor.RenderFrame` / `SkiaCompositor` gain
  optional `frames` + `plannedLayers` (default null = today's synchronous path, unchanged).
- New `Flicksy.VideoEditor/Playback` types: `VideoPrefetchPump`, `FrameBundle`, `IFrameBundleSource`,
  `ProjectBundleSource`, `IPlaybackFrameSource`, `IPlaybackFrameSink`.
- A clip may have up to two video decoders across its lifetime (the compositor's synchronous cache + the
  pump's), never decoding concurrently — play and scrub are mutually exclusive in time, so ADR 0005's
  no-thrash guarantee holds.
- **Scope of the win:** moves the codec decode + swscale convert + copy off the UI thread; the UI tick does
  only composite + present. It does **not** make the codec itself faster — sustained slower-than-realtime
  decode degrades to dropped frames with A/V lag bounded by the resync threshold (~1 s; lower preview quality
  is the lever), never a UI stall. A timeline edit
  mid-playback can tear the producer's `PlanFrame`; that frame is skipped (hold previous) and re-planned next
  cycle.
- The pump's queue/seek/generation/lifetime logic is unit-tested in isolation against a fake decode source
  (rent/return leak tracking + a rapid-seek stress); compositor/Skia/FFmpeg interop stays integration
  territory, consistent with ADR 0004.
- **Startup prebuffer + paused-state prefetch.** The pump's decoder cache is cold on first use (a
  separate cache from the scrub preview's), so the first decode includes opening the file (~0.5 s). Two
  mechanisms hide that. (1) When the playhead settles while paused, on pause, and on open,
  `PlaybackEngine` debounces a background `VideoPrefetchPump.Prefetch(frame, scale)` that warms the
  decoder and fills the buffer at the preview's scale — so the buffer is hot before the user presses
  play (pause→play and seek→play warm up identically). `Play` reuses
  it directly when the prefetch already covers the start position + scale (no drain). (2) As a safety
  net, `Play` still holds the playhead and audio until the pump reports the first frame ready
  (`HasReadyFrameAt`, capped at `MaxPrimeWaitMs`), so even an un-warmed play begins A/V-aligned instead
  of racing a cold decoder and drifting ~1 s behind. The debounce keeps rapid scrubbing from churning
  the decoder (prefetch only fires once the user parks). Trade-off: the pump now runs while paused and
  holds up to `DefaultDepth` decoded frames (bounded; shrinks with preview quality). A timeline edit
  while paused can leave the prefetched frames briefly stale on the next play — self-corrects within
  `DefaultDepth` frames as the producer re-plans, or immediately on any seek (which re-primes).
- **Deferred follow-ups:** byte-bounded queue depth at full resolution; caching `GraphicsClip`'s per-frame
  `RenderTargetBitmap` (a separate UI-thread cost); and audio-master A/V sync (still ADR 0005's deferred item).
