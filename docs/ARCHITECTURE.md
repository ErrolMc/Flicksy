# Flicksy — Architecture Reference

Token-optimized **index** to the current build. This file is the always-loaded map; per-editor detail lives in three leaf docs under [docs/architecture/](architecture/) (see §4) so a session loads only the part it needs. Read this first; jump to a leaf or a source file only when the change requires it. Keep all four updated whenever the structure changes — see [CLAUDE.md](../CLAUDE.md).

## 1. Solution shape

7 projects, defined in [Flicksy.slnx](../Flicksy.slnx). Four WinExes communicate by **launching each other as separate processes** (no project refs between them); both interactive editors reference the shared Drawing library, and PostSnip additionally references Icons.

| Project | OutputType | UI tech | TFM | Role |
| --- | --- | --- | --- | --- |
| [Flicksy.Agent](../Flicksy.Agent) | WinExe | WinForms (tray) | net10.0-windows | Background tray app. Registers global hotkey `Ctrl+Shift+Alt+S`. Launches `Flicksy.Snipper.exe`. |
| [Flicksy.Snipper](../Flicksy.Snipper) | WinExe | WPF + WinForms interop | net10.0-windows | Screen-region selection. Modes: **snip** (bitmap → PNG) or **record** (ffmpeg gdigrab → MP4). Launches `Flicksy.PostSnip.exe <mediaPath>`. |
| [Flicksy.PostSnip](../Flicksy.PostSnip) | WinExe | WPF (MVVM) | net10.0-windows | Image/video editor. Opens passed media, lets user annotate image or scrub video, saves output. References Drawing + Icons. |
| [Flicksy.VideoEditor](../Flicksy.VideoEditor) | WinExe | WPF (MVVM) | net10.0-windows | Multi-clip video editor. Arg-driven entry: no args → Welcome, `--new-video-project` → empty editor, positional path → editor with source. References Drawing. |
| [Flicksy.VideoEditor.Tests](../Flicksy.VideoEditor.Tests) | Library | NUnit 4 | net10.0-windows | Unit tests for video-editor logic. Currently covers `CompositionPlanner` math; `net10.0-windows` because the project references `Flicksy.VideoEditor` (the test code itself uses no Windows APIs). |
| [Flicksy.Drawing](../Flicksy.Drawing) | Library | WPF (MVVM) | net10.0-windows | Shared drawing primitives: `DrawingItem` hierarchy, tool system, undo manager, FFmpeg playback, `DrawingView` + selection overlay. References Icons. |
| [Flicksy.Icons](../Flicksy.Icons) | Library | none (assets only) | net10.0 | Icon PNGs + strongly-typed `Flicksy.Icons.Properties.Resources` accessor. Exposed to consumers as the alias `Images` via csproj-level `<Using Include="..." Alias="Images" />` (alias is `Images` not `Icons` because the `Flicksy.Icons` namespace would shadow `Icons` per C# §13.6 lookup order). |

**Convention**: no project refs between WinExes; class libraries (Drawing, Icons) may be referenced by any consumer. Drawing references Icons; Icons references nothing.

### 1.1 Inter-process contract

- **Agent → Snipper**: spawned with no args. Resolved via sibling-folder probing in [AgentApplicationContext.ResolveSnipperExecutablePath](../Flicksy.Agent/AgentApplicationContext.cs).
- **Agent → VideoEditor**: tray menu's `New Video Project` item spawns `Flicksy.VideoEditor.exe --new-video-project`. Resolved via [AgentApplicationContext.ResolveVideoEditorExecutablePath](../Flicksy.Agent/AgentApplicationContext.cs) (same sibling-folder probe pattern).
- **Snipper → PostSnip**: spawned with the media path as first arg (quoted). See [SnipperSessionController.TryLaunchPostSnipWithMedia](../Flicksy.Snipper/SnipperSessionController.cs).
- **PostSnip → VideoEditor**: `Launch in video editor` button on `PostSnipWindow` (visible only when `IsVideoLoaded`) spawns `Flicksy.VideoEditor.exe "<videoPath>"`. Handler sets `PreserveMediaFile=true` first so the temp video survives PostSnip closing. Resolved via [PostSnipViewModel.ResolveVideoEditorExecutablePath](../Flicksy.PostSnip/ViewModels/PostSnipViewModel.cs).
- **PostSnip startup arg parsing**: [App.ResolveStartupMediaPath](../Flicksy.PostSnip/App.xaml.cs) accepts `--launch-file <path>`, a positional first arg, or falls back to `LaunchPostSnipWithFilePath` in [appsettings.json](../Flicksy.PostSnip/appsettings.json) (used for dev launches without going through Snipper).
- **VideoEditor startup arg parsing**: [App.ResolveStartupMode](../Flicksy.VideoEditor/App.xaml.cs) returns a [StartupMode](../Flicksy.VideoEditor/StartupMode.cs) discriminated record — no args → `Welcome`, `--new-video-project` → `EmptyEditor`, positional first arg that's an existing file → `EditorWithSource(path)`. Unrecognized args fall back to `Welcome`. The `Welcome` window is resolved from the DI host; `EmptyEditor` and `EditorWithSource` are built by [IEditorFactory](../Flicksy.VideoEditor/Services/IEditorFactory.cs), which constructs the editor around the runtime-chosen [Project](../Flicksy.VideoEditor/Project/Project.cs) (empty or from source file) — see [video-editor.md](architecture/video-editor.md) §10.
- **Temp media files**: written to `%TEMP%/flicksy-snip-{guid}.png` or `%TEMP%/flicksy-recording-{guid}.mp4`. PostSnip deletes them on close unless `PreserveMediaFile` is set ([PostSnipViewModel.DeleteMediaFile](../Flicksy.PostSnip/ViewModels/PostSnipViewModel.cs)). Set by `App` when launched with an explicit arg so dev runs don't nuke user files, and by the PostSnip → VideoEditor handoff so the editor opens against the same temp file.

### 1.2 External dependencies

- **FFmpeg shared libs** (avcodec-*.dll etc.). Required by Drawing (FFMediaToolkit, used inside `FFmpegVideoPlayer`) and as a CLI by Snipper (gdigrab capture). Drawing probes a long list of locations via [FfmpegLocator](../Flicksy.Drawing/Media/FfmpegLocator.cs): `FFMPEG_HOME` env var, `PATH`, winget shared FFmpeg packages, `C:\ffmpeg\bin`, app-local `lib\ffmpeg`. PostSnip calls `FfmpegLocator.Initialize()` at startup.
- **NuGet by project**:
  - **Drawing** ([csproj](../Flicksy.Drawing/Flicksy.Drawing.csproj)): `CommunityToolkit.Mvvm`, `FFMediaToolkit` (+ its transitive `FFmpeg.AutoGen`, called directly by `HardwareMediaDecoder` — same shared DLLs). Builds with `<AllowUnsafeBlocks>`.
  - **PostSnip** ([csproj](../Flicksy.PostSnip/Flicksy.PostSnip.csproj)): `CommunityToolkit.Mvvm`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`. (FFMediaToolkit is transitive via Drawing.)
  - **VideoEditor** ([csproj](../Flicksy.VideoEditor/Flicksy.VideoEditor.csproj)): `CommunityToolkit.Mvvm`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, `SkiaSharp` (compositor), `NAudio` (playback audio output via WASAPI shared mode). (FFMediaToolkit is transitive via Drawing.)
  - **Resources** ([csproj](../Flicksy.Icons/Flicksy.Icons.csproj)): `System.Drawing.Common`, `System.Resources.Extensions`.

## 2. Flicksy.Agent (tray host)

Trivial. 3 files.

| File | Purpose |
| --- | --- |
| [Program.cs](../Flicksy.Agent/Program.cs) | WinForms entry point, runs `AgentApplicationContext`. |
| [AgentApplicationContext.cs](../Flicksy.Agent/AgentApplicationContext.cs) | Tray icon + context menu (Open Snipper / Exit). Owns the hotkey window. Resolves and starts `Flicksy.Snipper.exe`. |
| [HotKeyWindow.cs](../Flicksy.Agent/HotKeyWindow.cs) | `RegisterHotKey` P/Invoke for `Ctrl+Shift+Alt+S`. Calls back the supplied `Action` on `WM_HOTKEY`. |

## 3. Flicksy.Snipper (capture)

`App.xaml.cs` → constructs `SnipperSessionController` → shows `PreSnipOverlayWindow`. Shutdown is `OnExplicitShutdown` so windows can close/reopen without exiting.

| File | Purpose |
| --- | --- |
| [App.xaml.cs](../Flicksy.Snipper/App.xaml.cs) | Bootstraps the session controller. |
| [SnipperSessionController.cs](../Flicksy.Snipper/SnipperSessionController.cs) | State machine: PreSnip → (snip captured → launch PostSnip) **or** PreSnip → VideoRecordingOverlay → record → launch PostSnip. Shuts the app down when no overlays remain. |
| [ScreenRecorder.cs](../Flicksy.Snipper/ScreenRecorder.cs) | Spawns `ffmpeg` CLI with `gdigrab` at 30 fps, `libx264 veryfast yuv420p`. Stop = write `q` to stdin, with kill-fallback. Capture rect is clamped to `VirtualScreen` and rounded to even dimensions. |
| [Overlays/PreSnipOverlayWindow.xaml(.cs)](../Flicksy.Snipper/Overlays/PreSnipOverlayWindow.xaml.cs) | Full-screen overlay. Snapshots screen to `_backgroundBitmap` (so the cursor freezes), then user drags a selection. Mode buttons (Snip/Record) switch the on-confirm callback. |
| [Overlays/VideoRecordingOverlayWindow.xaml(.cs)](../Flicksy.Snipper/Overlays/VideoRecordingOverlayWindow.xaml.cs) | Sits over the chosen rect with Start/Stop + elapsed timer. Calls `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` so the overlay doesn't end up in the recording. |

Per-screen behavior: `PreSnipOverlayWindow` is created with the bounds of the screen the cursor is on at hotkey time (`Screen.FromPoint`), not the primary screen.


## 4. The editors — detailed maps

The interactive editors and the shared library they build on have their own maps, kept out of this index so a session loads only the part it needs:

| Map | Covers |
| --- | --- |
| [drawing.md](architecture/drawing.md) | **Flicksy.Drawing** — shared library: `DrawingItem` model, tool/interaction system, undo manager, FFmpeg playback, `DrawingView` canvas. Both editors depend on it. |
| [snip-editor.md](architecture/snip-editor.md) | **Flicksy.PostSnip** — image/video annotation editor: crop, image toolbar, video transport UI, save flow. Builds on drawing.md. |
| [video-editor.md](architecture/video-editor.md) | **Flicksy.VideoEditor** — multi-clip editor: document model, timeline, composition, playback, media bin, DI. Reuses drawing.md's undo/settings/ViewLocator. |

## 5. End-to-end flow (cheat sheet)



1. User presses `Ctrl+Shift+Alt+S`. [HotKeyWindow](../Flicksy.Agent/HotKeyWindow.cs) → [LaunchSnipper](../Flicksy.Agent/AgentApplicationContext.cs).
2. [PreSnipOverlayWindow](../Flicksy.Snipper/Overlays/PreSnipOverlayWindow.xaml.cs) appears on cursor's monitor with a frozen-screen background. User picks **Snip** or **Record** + drags a rect.
3. **Snip path**: bitmap → PNG in `%TEMP%`, copied to clipboard, then `Flicksy.PostSnip.exe "<path>"` ([SnipperSessionController.OnSnipCaptured](../Flicksy.Snipper/SnipperSessionController.cs)).
4. **Record path**: [VideoRecordingOverlayWindow](../Flicksy.Snipper/Overlays/VideoRecordingOverlayWindow.xaml.cs) → ffmpeg gdigrab → MP4 in `%TEMP%` → `Flicksy.PostSnip.exe "<path>"`.
5. PostSnip [App.OnStartup](../Flicksy.PostSnip/App.xaml.cs) initializes FFmpeg, builds the DI host, resolves `PostSnipWindow`, loads the media (`LoadImage` or `LoadVideoAsync`).
6. User annotates (Pen/Shape/Text/Erase via tools), navigates (pan/zoom/scrub), undoes (Ctrl+Z), saves (PNG or copied MP4) or cancels. PostSnip deletes the temp file on close unless `PreserveMediaFile` was set.

**VideoEditor entry paths**:

- **No args**: `Flicksy.VideoEditor.exe` → DI resolves [WelcomeWindow](../Flicksy.VideoEditor/Windows/WelcomeWindow.xaml.cs). Clicking `New Video Project` builds an empty editor via the injected `IEditorFactory` (`EditorRequest.Empty`), reassigns `MainWindow`, then closes Welcome.
- **Agent tray → VideoEditor**: tray menu's `New Video Project` item ([AgentApplicationContext.LaunchVideoEditor](../Flicksy.Agent/AgentApplicationContext.cs)) spawns `Flicksy.VideoEditor.exe --new-video-project` → [App.ResolveStartupMode](../Flicksy.VideoEditor/App.xaml.cs) → `EmptyEditor` → `VideoEditorWindow` built by `IEditorFactory` (empty project), Welcome skipped.
- **PostSnip → VideoEditor**: after a recording opens in PostSnip, the chrome's `Launch in video editor` button (video-only) sets `PreserveMediaFile=true` and spawns `Flicksy.VideoEditor.exe "<videoPath>"` ([PostSnipViewModel.LaunchInVideoEditor](../Flicksy.PostSnip/ViewModels/PostSnipViewModel.cs)) → `EditorWithSource(path)` → `VideoEditorWindow` built by `IEditorFactory` around `Project.CreateFromSourceFile(path)` so the video opens as the first clip on Video 1.

## 6. Flicksy.Icons (icon assets)

`net10.0` class library. No WPF; consumers convert `System.Drawing.Bitmap` to `ImageSource` via `BitmapExtensions.ToImageSource()` (in Drawing).

| File | Purpose |
| --- | --- |
| [Resources/*.png](../Flicksy.Icons/Resources) | Toolbar + shape + rotate-puck icons + media-bin audio glyph + video-editor title-bar settings gear, 20 PNGs. |
| [Resources/music-file.png](../Flicksy.Icons/Resources/music-file.png) | Audio-source glyph used by the media bin (`Images.music_file`). |
| [Resources/app-icon.ico](../Flicksy.Icons/Resources/app-icon.ico) / [.png](../Flicksy.Icons/Resources/app-icon.png) | The Flicksy app icon (multi-resolution `.ico`: 16/24/32/48/64/128/256, regenerated from the 500x500 `.png`). Source of truth for all four exe icons. |
| [Properties/Resources.resx](../Flicksy.Icons/Properties/Resources.resx) | ResXFileRef entries pointing at the PNGs. |
| [Properties/Resources.Designer.cs](../Flicksy.Icons/Properties/Resources.Designer.cs) | Strongly-typed `public` accessor. **Hand-edited from internal → public**; csproj `Generator` is `PublicResXFileCodeGenerator` so future regens stay public. |

The alias is **declared once per consumer csproj** as a csproj-level global using; no per-file `using` directive is needed:

```xml
<ItemGroup>
  <Using Include="Flicksy.Icons.Properties.Resources" Alias="Images" />
</ItemGroup>
```

[Flicksy.Drawing.csproj](../Flicksy.Drawing/Flicksy.Drawing.csproj), [Flicksy.PostSnip.csproj](../Flicksy.PostSnip/Flicksy.PostSnip.csproj), and [Flicksy.VideoEditor.csproj](../Flicksy.VideoEditor/Flicksy.VideoEditor.csproj) all declare this. Call sites use it bare: `Images.rotate`, `Images.circle`, `Images.cursor.ToImageSource()`, `Images.music_file.ToImageSource()`, etc.

The alias name is `Images` rather than `Icons` because a using-alias of `Icons` would be shadowed by the `Flicksy.Icons` namespace at every call site (C# §13.6 resolves namespace members before using aliases).

**App icon**: all four WinExes set `<ApplicationIcon>..\Flicksy.Icons\Resources\app-icon.ico</ApplicationIcon>` — a build-time file path, so Agent and Snipper consume it without a project reference to Icons. This embeds the icon into each exe (Explorer + taskbar), and WPF uses it as the default window icon for Snipper/PostSnip/VideoEditor windows automatically. The Agent tray `NotifyIcon` is the one place that needs explicit wiring (WinForms doesn't apply `ApplicationIcon` to a `NotifyIcon`): [AgentApplicationContext.LoadApplicationIcon](../Flicksy.Agent/AgentApplicationContext.cs) reads the embedded icon back out via `Icon.ExtractAssociatedIcon(Environment.ProcessPath)`.

## 7. Conventions seen in this codebase

- **MVVM via CommunityToolkit.Mvvm**: `[ObservableProperty]` on private fields generates the public property; `[RelayCommand]` on a private method generates a public `XxxCommand`. Don't hand-roll PropertyChanged.
- **DI composition root**: each editor exe registers services in an `AddXServices` extension ([AddVideoEditorServices](../Flicksy.VideoEditor/Services/ServiceCollectionExtensions.cs) / [AddPostSnipServices](../Flicksy.PostSnip/Services/ServiceCollectionExtensions.cs)); cross-cutting concerns sit behind interfaces (`ISettingsService`, `IOverlayService`, `IUndoService`, `IProjectSettingsService`) and are constructor-injected. A runtime-chosen `Project` flows through `IEditorFactory`, not the container; the root VM composes the per-document sub-VMs around it. Views resolve from VMs by convention via the shared [ViewLocator](../Flicksy.Drawing/Controls/ViewLocator.cs) (`{Name}ViewModel` → `{Name}View`). See [video-editor.md](architecture/video-editor.md) §10.
- **Tool extensibility**: new tools implement [IDrawingTool](../Flicksy.Drawing/Interaction/IDrawingTool.cs), get instantiated + registered in [DrawingView.OnDataContextChanged](../Flicksy.Drawing/Controls/DrawingView/DrawingView.xaml.cs), and depend on small `IXxxConfig` interfaces — not on `DrawingView` directly.
- **Undo commands**: state is mutated live during the gesture; the command is pushed at the **end** of the gesture with before/after snapshots. Multi-step bundles use [CompositeCommand](../Flicksy.Drawing/Undo/Commands/CompositeCommand.cs).
- **No emojis, comments only when WHY is non-obvious** (see existing comments — most explain a subtle invariant or a workaround).
- **Config applies at startup, not hot-reloaded** — shipped `appsettings.json` (dev/app knobs, e.g. PostSnip's `LaunchPostSnipWithFilePath`) is read once. User prefs (`%LOCALAPPDATA%\Flicksy\*.json` via [UserSettingsStore](../Flicksy.Drawing/Settings/UserSettingsStore.cs)) persist live on change, but their *effects* (decode mode, the future perf HUD) still apply only at the next startup.
- **Tests**: NUnit 4 in [Flicksy.VideoEditor.Tests](../Flicksy.VideoEditor.Tests). Scope is intentionally narrow — pure-logic units that benefit from regression coverage. Backend-touching code (WPF, Skia, FFmpeg) stays uncovered for now; the compositor's `CompositionPlanner` is the first beneficiary because the timeline math (active-clip detection, speed mapping) is otherwise easy to silently break.


## 8. Where to look for common changes

Cross-cutting changes are below; editor-specific changes route to a leaf doc's own "where to look" table.

| Change request | Primary file(s) |
| --- | --- |
| Change the global hotkey | [HotKeyWindow](../Flicksy.Agent/HotKeyWindow.cs). |
| Change capture format/quality | [ScreenRecorder.BuildArguments](../Flicksy.Snipper/ScreenRecorder.cs). |
| Add a new shared icon | drop PNG into [Flicksy.Icons/Resources/](../Flicksy.Icons/Resources), add entry to [Resources.resx](../Flicksy.Icons/Properties/Resources.resx), regenerate `Resources.Designer.cs` (or hand-add a public property). Consume via `Images.<name>`. |
| Change the app / tray icon | replace [Flicksy.Icons/Resources/app-icon.ico](../Flicksy.Icons/Resources/app-icon.ico) (all four exes point `<ApplicationIcon>` at it); tray icon loads it in [AgentApplicationContext.LoadApplicationIcon](../Flicksy.Agent/AgentApplicationContext.cs). See §6. |
| Anything in the snip editor (crop, annotate toolbar, video transport, save) | [snip-editor.md](architecture/snip-editor.md) §6 |
| Anything in the shared canvas (drawing items, tools, undo, playback engine) | [drawing.md](architecture/drawing.md) §8 |
| Anything in the video editor (timeline, clips, composition, playback, media bin) | [video-editor.md](architecture/video-editor.md) §11 |
