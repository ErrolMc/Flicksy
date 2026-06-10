# Hardware video decode via FFmpeg.AutoGen (CUDA / D3D11VA)

## Status

Accepted. Addresses the limitation ADR 0009 called out explicitly: the decode-ahead pump "does
**not** make the codec itself faster."

## Context

ADR 0009 moved video decode off the UI thread, but the codec is the bottleneck: ~17–19 fps
sequential software decode for 1080p60 H.264 — slower than realtime — so the pump's bounded-resync
path drops frames continuously on demanding sources and lower preview quality is the only lever.

FFMediaToolkit decodes CPU-only and exposes no hwaccel hooks (no `hw_device_ctx`, no
decoder-by-name). Its sole dependency, **FFmpeg.AutoGen 7.1.1**, binds the exact FFmpeg 7.1 shared
DLLs the app already loads via `FfmpegLocator` and exposes the full hwaccel API — so hardware
decode needs no new native dependency, only code written one layer below FFMediaToolkit.

## Decision

A hardware video decoder behind the existing `IMediaDecoder` seam, used by the video seams with
silent per-source fallback to the software decoder.

- **`HardwareMediaDecoder`** (`Flicksy.Drawing/Media/`, `unsafe`, FFmpeg.AutoGen direct): demux +
  decode with a per-instance hardware device (`av_hwdevice_ctx_create`), trying device types in
  order **CUDA, then d3d11va** (a one-time static probe finds the working set; each source then
  attempts a full open per type). `get_format` accepts whichever hw pixel format the attached
  device offers (`AV_PIX_FMT_CUDA` / `AV_PIX_FMT_D3D11`; delegate rooted in a static field — the
  `_func` conversion does not root it), `thread_count = 1` (frame threading buys nothing under
  hwaccel), `extra_hw_frames = 4`. `FfmpegLocator.Initialize` now also sets
  `FFmpeg.AutoGen.ffmpeg.RootPath` so direct AutoGen calls bind even when they are the process's
  first FFmpeg touch.
- **Read-cursor semantics mirror `FFmpegMediaDecoder`** so hw and sw cursors are interchangeable:
  forward reads cheap, jumps past 500 ms seek back to a keyframe (`AVSEEK_FLAG_BACKWARD`) and
  decode-discard to the target, `best_effort_timestamp` with `start_time` normalization, silent
  null on decode failure. The last transferred frame is cached and re-served (`[start, end)`
  containment) — timeline framerates above the source framerate re-request the same source frame,
  and without the cache every repeat would cost a GOP re-decode.
- **CPU readback, video-only.** `av_hwframe_transfer_data` pulls the frame to system memory
  (NV12/P010 — self-described, never assumed); one `sws_getCachedContext`/`sws_scale` pass does
  both the BGRA32 conversion and the ADR 0008 decode-scale downscale into the `ArrayPool` buffer.
  The GPU surface is unref'd every cycle (the d3d11va pool is fixed at open). Audio is never
  decoded here (`HasAudio` false) — audio seams keep `FFmpegMediaDecoder`'s proven
  resample/remix cursor.
- **Selection + fallback.** `MediaDecoderCache(preferHardwareVideo)`: the video seams opt in
  (`ProjectBundleSource` — covering the playback pump and the scrub worker — and
  `SkiaCompositor`'s synchronous provider); `AudioMixer`'s cache stays software. Three gates, all
  at construction and tried per device type: a one-time static device probe
  (`HardwareMediaDecoder.IsAvailable`), a codec-level `avcodec_get_hw_config` check, and a
  **first-frame probe decode** verifying the codec actually produced a hardware pixel format —
  profile-level refusals (e.g. H.264 High 4:4:4) only surface at first decode, and they must fail
  at open (→ next device type, then `FFmpegMediaDecoder` for that source, Debug-logged) rather
  than mid-playback. The kill switch and A/B lever is `DisableHardwareDecode` in the video
  editor's appsettings.json, pushed once at startup into `HardwareMediaDecoder.Disabled` — the
  Drawing library reads no configuration itself, and set-once keeps the no-live-config convention.

## Why

- **Raw AutoGen under FFMediaToolkit, not a library swap.** FFMediaToolkit keeps doing what it's
  good at (audio cursor, the software path, probing); only the hot video path is reimplemented.
  Same DLLs, no new install requirement, and the package was already on the dependency closure.
- **CUDA before d3d11va.** Both drive the same NVDEC silicon on NVIDIA, but the GPU→CPU readback
  differs by an order of magnitude: FFmpeg's d3d11va `av_hwframe_transfer_data` copies to a
  staging texture and `Map`s it, which stalls until the GPU pipeline drains — measured **8–13
  ms/frame at 1080p, ~80% of the total decode cost** — while the CUDA path's transfer is a plain
  `cuMemcpy` (~1 ms). d3d11va stays in the chain as the vendor-neutral fallback (AMF/UVD on AMD,
  QuickSync on Intel). Of the remaining alternatives: `dxva2` is the legacy D3D9 path, `d3d12va`
  is too new for field mileage, and Vulkan video decode is driver-roulette in FFmpeg 7.1.
- **Fail-hard `get_format`, fail-at-construction probe.** Letting FFmpeg fall back to a software
  format inside the hw decoder would crawl on `thread_count = 1`; refusing (`AV_PIX_FMT_NONE`)
  routes the source to the proper multithreaded software decoder. Playback can never get worse
  than before this change.
- **Per-instance D3D11 device, not process-shared.** Isolates failures, ties VRAM lifetime exactly
  to the decoder, and avoids cross-thread contention between the pump producer and the scrub
  worker. A shared `AVBufferRef` device is a noted follow-up if per-instance cost shows up.
- **Readback is acceptable for now.** NVDEC decodes 1080p60 at several multiples of realtime;
  transfer + NV12→BGRA swscale costs a few ms per 1080p frame — well under the 16.7 ms realtime
  budget the software path missed by ~3×. Eliminating the readback entirely means GPU-resident
  frames into a GPU compositor (`Dx11Compositor` behind `ICompositor`) — that is the recorded
  follow-up, out of scope here.

## Consequences

- New file `HardwareMediaDecoder.cs`; `Flicksy.Drawing` gains `<AllowUnsafeBlocks>`. FFmpeg.AutoGen
  is consumed via its transitive reference (an explicit `<PackageReference Include="FFmpeg.AutoGen"
  Version="7.1.1" />` pin is recommended hygiene).
- Each open hardware decoder holds a D3D11 device + fixed surface pool (order ~50–70 MB VRAM for
  1080p H.264). Bounded by the existing seam lifetimes (caches die with the engine/preview);
  eviction and a shared device are deferred until they matter.
- ADR 0009's resync machinery stays but should now be dormant for sources the GPU decodes faster
  than realtime. Measured on a 1080p60 H.264 gameplay clip, per-`Acquire` cost on the pump thread
  (one timeline frame = two source frames at the 30 fps project rate; ADR 0009's ~19 fps figure
  was end-to-end displayed fps, which overstated the decode share): software ~17 ms; d3d11va
  hardware ~15 ms = decode 0.5 + **transfer 8–13** + convert 2 (the readback Map stall that
  forced the CUDA-first decision); CUDA hardware **~7 ms warm** (decode 4 + transfer 0.8 +
  convert 2), settling to ~17 ms as the lightly-loaded GPU downclocks — decode and transfer
  inflate by the same ~3x, a power-state artifact, not a throughput ceiling (the 30 fps budget is
  33 ms). Wall-clock parity with software on light content is expected; the hardware path's win
  there is freeing the CPU cores, and it holds its number where software collapses (HEVC, 4K,
  multi-clip).
- If NVDEC pacing ever becomes the floor, the next lever inside this design is eager decoder
  input feeding (send packets until `EAGAIN` so decode overlaps the transfer + convert of the
  previous frame) before reaching for the GPU compositor.
- Native-interop risk (a pointer bug is an access violation, not an exception) is contained by the
  ownership discipline in the decoder (receive-first pump, unref-every-cycle, ctor failure runs
  the dispose routine) and the kill switch as a user-facing emergency exit. The decoder is not
  unit-tested, consistent with the repo's native-code testing convention.
- PostSnip's `FFmpegVideoPlayer` is untouched; the decoder lives in `Flicksy.Drawing` so the
  issue #23 unification can adopt it later.
