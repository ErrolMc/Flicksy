# Flicksy

A Windows desktop tool for capturing the screen, annotating it, and editing video — built on .NET 10 and WPF.

![Platform](https://img.shields.io/badge/platform-Windows-0078D6)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![UI](https://img.shields.io/badge/UI-WPF-512BD4)
![Language](https://img.shields.io/badge/language-C%23-178600)

Flicksy has two surfaces, each running as its own process:

- **Snip editor** — a fast capture-and-annotate flow. Press a hotkey, drag out a region, and either grab a still image or record it to video. The captured media opens in an editor where you draw on top (pen, shapes, text), crop, and save out. One image or video in, annotations on top, file out.
- **Video editor** — a non-linear editor (NLE) for assembling clips on a multi-track timeline. Import media, drag clips onto tracks, trim and arrange them, and play back or scrub a live composited preview with audio.

The two surfaces share rendering primitives (drawing items, tools, undo, FFmpeg playback) through the `Flicksy.Drawing` class library, but otherwise run independently — each has its own document, its own undo stack, and its own FFmpeg init. They communicate only by passing file paths and CLI arguments to one another.

## How it works

The normal entry point is a background tray app:

1. **`Flicksy.Agent`** sits in the system tray and registers the global hotkey **`Ctrl+Shift+Alt+S`**.
2. Pressing the hotkey launches **`Flicksy.Snipper`**, which dims the screen the cursor is on and lets you drag out a region in **Snip** mode (bitmap → PNG) or **Record** mode (FFmpeg `gdigrab` → MP4).
3. The captured file opens in **`Flicksy.PostSnip`**, the snip editor, where you annotate, crop, and save (or, for a recording, hand it off to the video editor).
4. **`Flicksy.VideoEditor`** can be opened on its own from the tray menu, from a recording in PostSnip, or directly with a file path.

```
                 Ctrl+Shift+Alt+S
   Flicksy.Agent ───────────────► Flicksy.Snipper ──┐
   (tray, hotkey)                 (capture region)   │ media path
        │ "New Video Project"                        ▼
        │                                       Flicksy.PostSnip
        ▼                                       (annotate / crop)
   Flicksy.VideoEditor ◄──────────────────────────┘  "Launch in video editor"
   (multi-track NLE)
```

## Status

| Surface | State |
| --- | --- |
| Snip editor (`Flicksy.PostSnip`) | Working — capture image or video, annotate (pen / shapes / text / erase), crop, pan / zoom / scrub, undo / redo, save PNG or copy MP4. |
| Video editor (`Flicksy.VideoEditor`) | In active development — create a project, import media into the bin, drag clips onto timeline tracks, edit the timeline (move / trim / split / delete, with multi-select), real-time playback with audio, scrub a live composited preview, rename and detach-audio on clips, mute / lock / disable tracks. |

The video editor is being built out in numbered slices tracked on the [GitHub issue tracker](https://github.com/ErrolMc/Flicksy/issues). Not yet implemented: drawing on clips, transitions, the per-clip inspectors, export to MP4, and project save/load. See [docs/video-editor/PLAN.md](docs/video-editor/PLAN.md) for the roadmap.

## Projects

The solution ([Flicksy.slnx](Flicksy.slnx)) has seven projects. The four `WinExe`s never reference each other — they launch each other as separate processes.

| Project | Type | Role |
| --- | --- | --- |
| [Flicksy.Agent](Flicksy.Agent) | WinExe (tray) | Background tray host. Registers the global hotkey and launches the snipper / video editor. |
| [Flicksy.Snipper](Flicksy.Snipper) | WinExe | Screen-region selection. Captures a still (PNG) or records video (FFmpeg `gdigrab` → MP4). |
| [Flicksy.PostSnip](Flicksy.PostSnip) | WinExe | The snip editor. Opens captured media, annotates / crops, saves output. |
| [Flicksy.VideoEditor](Flicksy.VideoEditor) | WinExe | The multi-clip video editor (timeline, media bin, compositor preview). |
| [Flicksy.Drawing](Flicksy.Drawing) | Library | Shared primitives: drawing items, tools, undo manager, FFmpeg playback, the drawing canvas. Used by both editors. |
| [Flicksy.Icons](Flicksy.Icons) | Library | Icon PNG assets and a strongly-typed accessor. |
| [Flicksy.VideoEditor.Tests](Flicksy.VideoEditor.Tests) | NUnit 4 | Unit tests for video-editor logic — composition planner, timeline hit-testing, the playback prefetch pump, and timeline edits (add / move / trim / split / delete / detach-audio). |

For the full structural map — every namespace folder, drawing tool, undo command, view model, and the file behind each — read **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**.

## Requirements

- **Windows.** All projects target `net10.0-windows` and use WPF / WinForms, so they build and run on Windows only.
- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** (or Visual Studio 2022/2026 with the .NET 10 workload).
- **FFmpeg — a *shared* build.** This is the easy thing to get wrong. Flicksy links against the FFmpeg **shared libraries** (`avcodec-*.dll`, `avformat-*.dll`, etc.) through [FFMediaToolkit](https://github.com/radek-k/FFMediaToolkit) for decode and playback, and the snipper also invokes the `ffmpeg` CLI with `gdigrab` to record the screen. The default `winget install ffmpeg` installs a *statically-linked* build with no shared DLLs and **will not work**. Install a shared build instead:

  ```powershell
  winget install Gyan.FFmpeg.Shared --version 7.1.1
  ```

  Flicksy then finds it automatically. FFMediaToolkit binds to a specific FFmpeg major version, so install the pinned `7.x` build above rather than the latest — a newer major may fail to load.

  If you install it elsewhere, the probe order is: `FFMPEG_HOME` (env var) → every directory on `PATH` → the winget `*FFmpeg.Shared*` package location → `C:\ffmpeg\bin` → an app-local `lib\ffmpeg` folder next to the executable. Any directory containing `avcodec-*.dll` will do.

## Building

```powershell
dotnet build Flicksy.slnx
```

## Running

For normal use, run the tray agent and drive everything from the hotkey:

```powershell
dotnet run --project Flicksy.Agent
```

The agent appears in the system tray. Press **`Ctrl+Shift+Alt+S`** to start a capture, or use the tray menu to open the snipper or a new video project.

To work on a single surface directly during development, run it on its own:

```powershell
# Video editor (no args → Welcome window; pass a file path to open it as the first clip)
dotnet run --project Flicksy.VideoEditor

# Snip editor against an existing image or video
dotnet run --project Flicksy.PostSnip -- "C:\path\to\media.png"
```

> `Flicksy.PostSnip` can also be pointed at a file for dev launches via the `LaunchPostSnipWithFilePath` setting in [Flicksy.PostSnip/appsettings.json](Flicksy.PostSnip/appsettings.json), so you can run it without going through the snipper.

## Tests

```powershell
dotnet test
```

Test scope is intentionally narrow — pure logic that benefits from regression coverage: the composition planner, timeline hit-testing and edit math (move / trim / split / delete / detach-audio), and the playback prefetch pump (against a fake decode source). Backend-touching code (WPF, Skia, FFmpeg) is left to manual verification.

## Tech stack

| Area | Choice |
| --- | --- |
| Runtime / language | .NET 10, C# |
| UI | WPF (editors, snipper) and WinForms (tray, interop) |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| Video decode / playback | [FFMediaToolkit](https://github.com/radek-k/FFMediaToolkit) over FFmpeg |
| Video compositor | [SkiaSharp](https://github.com/mono/SkiaSharp) (CPU backend) |
| Audio output | [NAudio](https://github.com/naudio/NAudio) (WASAPI shared mode) |
| DI / hosting | `Microsoft.Extensions.Hosting` + `DependencyInjection` |
| Tests | NUnit 4 |

## Documentation

| Doc | What it is |
| --- | --- |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | The structural map of the codebase — read this before exploring. |
| [docs/CONTEXT.md](docs/CONTEXT.md) | The domain glossary — canonical names for the concepts (snip vs. video editor, Project / Track / Clip, the compositor, etc.). |
| [docs/adr/](docs/adr) | Architecture Decision Records for the load-bearing, hard-to-reverse choices. |
| [docs/video-editor/](docs/video-editor) | The video editor's implementation plan and timeline layout notes. |
| [CLAUDE.md](CLAUDE.md) | Conventions for working in the repo (kept short; points at the docs above). |

## License

Flicksy is licensed under the **GNU General Public License v3.0** — see [LICENSE](LICENSE) for the full text.

In short: you are free to use, study, share, and modify it, but any version you distribute (including modified ones) must also be released under the GPLv3 with its source available.
