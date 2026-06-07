# Real-playback threading and audio architecture

## Decision

#11 wires the transport to drive the compositor for real playback. The threading model [ADR 0004](0004-compositor-design.md) deferred to #11 is resolved here, along with the audio output path and a refactor that splits the audio mix out of `ICompositor`.

**Playback engine.** A new `PlaybackEngine` (`Flicksy.VideoEditor/Playback/`) owns the playback clock and the audio output. `VideoEditorViewModel` constructs and disposes it (same lifetime as the `ICompositor`). `TransportViewModel`'s play/pause/seek/step commands delegate to it; it writes `Playhead` / `IsPlaying` back, which the preview, timeline, and ruler already observe — no new binding wiring.

**v1 is UI-thread playback; off-thread decode-ahead is deferred.** The clock runs on a `CompositionTarget.Rendering` hook driven by a `Stopwatch` (the proven `FFmpegVideoPlayer.OnRendering` pattern). Each tick selects the timeline frame from elapsed time and, only when the frame changes, calls `ICompositor.RenderFrame` on the UI thread — reusing the caller-owned, unfrozen `WriteableBitmap` from ADR 0004 verbatim. This satisfies the `GraphicsClip` dispatcher requirement and the unfrozen-bitmap thread-affinity constraint for free. The off-thread decode-ahead worker (and its cross-thread bitmap hand-off) described in the original #11 comment is **phase 2**, taken on when measurements demand it. *(Now implemented — decode-only off-thread, compositing still on the UI thread — see [ADR 0009](0009-decode-ahead-pump.md).)*

**A/V sync: system clock, not audio clock.** Video frames are timed off the `Stopwatch`; audio is pushed open-loop to the output device. This avoids depending on the device's playback position. The cost is a small constant offset (output buffer depth) and slow drift (~1 frame per 10 min of *continuous* play at typical sound-card accuracy) — but the clock re-syncs on every pause / seek / scrub, so the only failure mode is long uninterrupted review playback. Audio-master sync (slaving video to the device position) is the deferred fix.

**Audio output: NAudio `WasapiOut` (shared mode).** WPF `MediaElement` / `MediaPlayer` are file/URI players and cannot consume the live PCM mix the compositor produces — architecturally disqualified. NAudio is pull-based: the device calls `ISampleProvider.Read` on its own thread. An adapter batches per-frame `RenderAudio` output (one video frame's worth each) into the arbitrary sample counts the device requests, advancing its own sample counter as the audio clock.

**`ICompositor` split into `ICompositor` + `IAudioMixer`.** `RenderAudio` moves out of `ICompositor` / `SkiaCompositor` into a new non-Skia `AudioMixer : IAudioMixer`; `SkiaCompositor` becomes video-only. A shared `MediaDecoderCache` helper (extracted from `SkiaCompositor.TryGetOrCreateDecoder` plus the decoder dictionary) backs both seams, one instance each. Two needs drive this: (1) video renders on the UI thread while audio mixes on NAudio's thread — two calls in flight, which a single-call-in-flight compositor cannot serve from one shared decoder cache; (2) a `Streams=Both` clip needs independent video and audio cursors, because FFmpeg seeks the container, not a stream, so one `MediaFile` serving both thrashes. Separate caches give each stream its own `FFmpegMediaDecoder` (its own `MediaFile`), on its own thread.

**Decoders read forward.** `FFmpegMediaDecoder` (audio and video) maintains a read cursor: during playback it reads the next frame / samples *forward* (`TryGetNextFrame`) and seeks (`GetFrame`) only on a discontinuity (scrub, click, loop-to-start). The audio path persists leftover source samples and linear-resampler state across calls for sample-continuous output; resampling is skipped when source rate == target rate; remix is mono→duplicate, stereo→passthrough, >2→front-two channels. Without the cursor, audio clicks at every frame boundary and video stutters from a seek-per-frame.

**Playback feel.** Scrub renders synchronously on the UI thread (kept responsive by the read-forward decoder); seek is frame-accurate; at project end playback holds the last frame with `IsPlaying=false` and Play-from-end restarts at frame 0 (mirrors `FFmpegVideoPlayer`); spacebar toggles play/pause at the window level but is ignored while a text box has focus.

## Why

- **UI-thread v1 honors ADR 0004's perf decision.** 0004 rejected per-frame allocate-and-freeze in favor of a caller-owned, reused, *unfrozen* bitmap — which is single-thread by construction. Off-thread playback would force either reintroducing that allocation or a ping-pong / raw-buffer scheme, plus a `GraphicsClip` dispatcher bounce. UI-thread playback collapses all of that to nothing for v1; the read-forward decoder is what makes it fast enough.
- **System-clock sync defers a dependency, not a corner.** Going Option 1 → audio-master later only needs the device position; at that point both soft-resync and full slaving are cheap. Deferring costs nothing structural.
- **Splitting the mixer matches ADR 0004's own intent.** 0004 deliberately kept `RenderFrame` and `RenderAudio` as independent calls ("each caller composes what it needs"). The split makes that physical and is the only clean way to run video and audio on different threads against a single-call-in-flight design.

## Consequences

- **Refactor of shipped #10 code.** `ICompositor` loses `RenderAudio`; `SkiaCompositor` becomes video-only; new `IAudioMixer` / `AudioMixer` + `MediaDecoderCache` land. `PreviewViewModel` is unaffected (it only calls `RenderFrame`). Supersedes the API-surface and Threading sections of ADR 0004.
- **New NuGet dependency** `NAudio` on `Flicksy.VideoEditor`.
- **`FFmpegMediaDecoder` gains audio decode** (remix + linear resample + read cursor) and a video read cursor. The split lets each cache later open `MediaMode.Video` / `MediaMode.Audio` to skip the unused stream.
- **Two decoder instances per `Streams=Both` clip** (one video, one audio) — the correct independent-cursor behavior, at the cost of a second file handle.
- **Deferred follow-ups:** ~~off-thread decode-ahead worker~~ (done — [ADR 0009](0009-decode-ahead-pump.md)); audio-master A/V sync (still deferred). Both were documented phase-2 work, additive rather than rewrites — the decode-ahead worker slotted between the clock and `RenderFrame` without changing the seams, as predicted.
