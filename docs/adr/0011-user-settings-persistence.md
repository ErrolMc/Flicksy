# User settings persistence (writable JSON preferences)

## Status

Accepted. Relocates the decode kill switch introduced in [ADR 0010](0010-hardware-video-decode.md)
from `appsettings.json` into a writable user-settings file.

## Context

`ISettingsService` was a read-once view over `appsettings.json` (shipped in the output dir, read
through `IConfiguration`). That is fine for dev/app knobs but wrong for user preferences: the output
dir can be read-only (e.g. under Program Files) and is overwritten on redeploy.

Two user-facing toggles needed to persist across launches: GPU-vs-CPU video decode (previously the
`DisableHardwareDecode` appsettings kill switch from ADR 0010) and "show performance stats" (an
unwired placeholder on the Settings overlay). No user-data-directory convention existed — the only
writable path the app used was `%TEMP%` for media.

## Decision

- A writable per-app JSON preferences file under `%LOCALAPPDATA%\Flicksy\` (`video-editor.json`,
  `post-snip.json`), read and written by a shared `UserSettingsStore` in `Flicksy.Drawing`.
- `ISettingsService` exposes an observable `Current` settings object (`VideoEditorSettings` /
  `PostSnipSettings`, a CommunityToolkit `ObservableObject`) loaded once at startup and **auto-saved
  on every property change**. The Settings shell-overlay tile two-way binds straight to it.
- **Effects still apply at startup, not live.** The decode preference is read once in
  `App.OnStartup` into `HardwareMediaDecoder.Disabled` — this moves ADR 0010's kill switch from
  `DisableHardwareDecode` (appsettings.json) to `UseHardwareDecode` (the user store, default true,
  inverted at the push). No decoder-cache invalidation: a decode change applies on the next launch.
- `UserSettingsStore.Load` never throws (missing/corrupt file -> defaults); `Save` writes atomically
  (temp file + move) and swallows IO errors, mirroring the hardware-decode fallback's best-effort
  posture.

## Why

- **LocalApplicationData, not Roaming** — GPU availability is machine-specific, so the decode
  preference must not roam to a machine with a different GPU.
- **Shared primitive in Drawing, not duplicated** — Drawing is the only library both WinExes
  reference (the repo forbids WinExe->WinExe project refs), so one shared class keeps the mechanism
  byte-for-byte identical across the two editors instead of two copies that drift.
- **Per-exe files** — the two processes have independent settings shapes; separate files avoid both
  clobbering and a shared schema that couples them.
- **Apply-on-restart, not live** — matches the repo's "config applied at startup" convention and
  avoids invalidating/rebuilding the per-`Clip.Id` `MediaDecoderCache` mid-session. Live switching
  is a deferred follow-up that would add cache invalidation.
- **appsettings.json stays for genuine dev/app knobs** (PostSnip's `LaunchPostSnipWithFilePath`);
  user prefs are a separate concern. VideoEditor's `appsettings.json` was removed because its only
  key moved to the store and nothing else read `IConfiguration`.

## Consequences

- The file location and key names are now a compatibility surface: renaming them later orphans
  users' saved preferences.
- The file is written lazily (on the first change), so it does not exist until the user toggles
  something; `Load` tolerates absence by returning defaults.
- `PostSnipSettings` starts empty — intentional scaffolding so the mechanism is mirrored and ready;
  nothing is written for PostSnip until it gains a real option.
- ADR 0010's kill-switch detail (`DisableHardwareDecode` in appsettings.json) is superseded here.
  The static `HardwareMediaDecoder.Disabled` contract is unchanged — `App.OnStartup` only sources
  its value differently.
- Follow-ups: a performance-stats HUD reads `ShowPerformanceStats` from the store; live decode
  switching would add `MediaDecoderCache` invalidation.
