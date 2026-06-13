# Flicksy.PostSnip — snip editor

Detailed map for **Flicksy.PostSnip**, the image/video annotation editor launched after a snip or recording. It owns the snip-specific orchestration — crop, image toolbar, video transport UI, save — and builds on the shared [drawing.md](drawing.md) library for rendering, the tool system, undo, and playback. Part of the [architecture index](../ARCHITECTURE.md). Section numbers are local to this file. Keep it updated per [CLAUDE.md](../../CLAUDE.md).

Convention: anything the video editor will also reuse lives in Drawing; anything specific to the snip flow (crop, image toolbar, video transport UI) stays here.

## 1. Layout

```
Flicksy.PostSnip/
├── App.xaml(.cs)
├── PostSnipWindow.xaml(.cs)
├── appsettings.json
├── Services/        ← ISettingsService + AddPostSnipServices (mirrors VideoEditor — video-editor.md §10)
├── ViewModels/
│   PostSnipViewModel
│   ImageEditToolsViewModel
│   CropOverlayViewModel
│   {Pen,Shape,Text,Fill,Outline}SettingsViewModel
├── Controls/
│   ImageEditToolsView
│   {Pen,Shape,Text,Fill,Outline}SettingsView
│   CropOverlayView
│   VideoPlaybackOverlay
└── Undo/Commands/
    CropCommand
```

The canvas, drawing items, tools, undo stack, and FFmpeg playback live in the shared library — see [drawing.md](drawing.md). `CropCommand` is the one snip-specific undo command (listed in drawing.md §5).

## 2. ViewModels

| ViewModel | Owns / Coordinates |
| --- | --- |
| [PostSnipViewModel](../../Flicksy.PostSnip/ViewModels/PostSnipViewModel.cs) | Root VM. Holds `Player`, `ImageEditTools`, `Drawing`, `SelectionOverlay`. Loads image/video, raises `SaveDialogRequested`/`CloseRequested`/`ErrorOccurred` for the window code-behind. Cross-VM wiring: subscribes to `Drawing.SelectedItem`/`EditingTextItem` + `ImageEditTools.SelectedTool`/`IsSelectActive` to keep selection overlay + text-edit lifecycle consistent. |
| [ImageEditToolsViewModel](../../Flicksy.PostSnip/ViewModels/ImageEditToolsViewModel.cs) | `SelectedTool` enum (`ImageEditTool.Select/Pen/Erase/Shapes/Text/Crop`). Pen/Shape/Text sub-settings VMs. Popup open-state with 250ms debounce to stop reopen-on-close cycles. |
| [CropOverlayViewModel](../../Flicksy.PostSnip/ViewModels/CropOverlayViewModel.cs) | Non-destructive crop state: `ImageWidth`/`Height`, `CommittedCrop` (persistent), `WorkingCrop` (mid-edit), `IsActive`. `EffectiveCrop`/`CurrentViewBounds` are what the view+window read. `BeginEdit`/`CommitEdit`/`CancelEdit` drive lifecycle; `CommitEdit` pushes a `CropCommand` if the rect changed. Holds a ref to `Drawing.History` for the push. |
| [PenSettingsViewModel](../../Flicksy.PostSnip/ViewModels/PenSettingsViewModel.cs) / [ShapeSettingsViewModel](../../Flicksy.PostSnip/ViewModels/ShapeSettingsViewModel.cs) / [TextSettingsViewModel](../../Flicksy.PostSnip/ViewModels/TextSettingsViewModel.cs) | Tool-specific settings (size, color, font, etc). Shape+Text own a [FillSettingsViewModel](../../Flicksy.PostSnip/ViewModels/FillSettingsViewModel.cs) + [OutlineSettingsViewModel](../../Flicksy.PostSnip/ViewModels/OutlineSettingsViewModel.cs). Both fill/outline VMs expose `SyncFromBrush(...)` so the popup can reflect the selected item's existing style. |

## 3. Controls

| Control | Notes |
| --- | --- |
| [ImageEditToolsView](../../Flicksy.PostSnip/Controls/ImageEditToolsView.xaml.cs) | Centered toolbar. Click on already-active Pen/Shapes/Text toggles its settings popup. Opening the Text popup begins a `TextStyleCommand` snapshot; closing pushes the diff. |
| [CropOverlayView](../../Flicksy.PostSnip/Controls/CropOverlayView.xaml.cs) | Snipping-tool-style crop UI: dim shade over the image area outside the crop, white outline, L-shaped corner brackets, edge midpoint markers. Visible only while `CropOverlayViewModel.IsActive`. Owns all crop gestures (corner/edge resize, move, draw-new). Uses `ContentToViewport` like `SelectionOverlayView` so the handles render at fixed pixel size. |
| [VideoPlaybackOverlay](../../Flicksy.PostSnip/Controls/VideoPlaybackOverlay.xaml.cs) | Transport bar. Two scrub sources: slider (mouse drag) and keyboard (Left/Right arrows step one frame). Scrub targets coalesce through a capacity-1 `Channel<long>` with `DropOldest`; a worker calls `IVideoPlayer.Seek` and yields ~16ms so the render loop can present the seeked frame. |
| [FillSettingsView](../../Flicksy.PostSnip/Controls/FillSettingsView.xaml) / [OutlineSettingsView](../../Flicksy.PostSnip/Controls/OutlineSettingsView.xaml) / [PenSettingsView](../../Flicksy.PostSnip/Controls/PenSettingsView.xaml) / [ShapeSettingsView](../../Flicksy.PostSnip/Controls/ShapeSettingsView.xaml) / [TextSettingsView](../../Flicksy.PostSnip/Controls/TextSettingsView.xaml) | Popup content for the toolbar. |

## 4. Window (`Flicksy.PostSnip/PostSnipWindow`)

[PostSnipWindow.xaml](../../Flicksy.PostSnip/PostSnipWindow.xaml) is the only top-level window:
- DockPanel: top chrome (New / tools panel / Save+Cancel) + central viewport.
- `<Window.InputBindings>` wires `Ctrl+Z` / `Ctrl+Y` to `Drawing.History.UndoCommand`/`RedoCommand`.
- Image viewport uses a `Canvas` (not Grid) so the image renders at natural pixel size, with `ScaleTransform`+`TranslateTransform` doing fit/pan/zoom.
- Code-behind ([.xaml.cs](../../Flicksy.PostSnip/PostSnipWindow.xaml.cs)) handles: dark titlebar via `DwmSetWindowAttribute`, mouse-wheel zoom/pan, horizontal wheel via `WM_MOUSEHWHEEL` hook, middle-button pan, Delete-key to delete selected item, and adapters for `SaveDialogRequested`/`CloseRequested`/`ErrorOccurred` from the VM.
- `TryAutoFit` and `ClampOffsets` use `CropOverlayViewModel.CurrentViewBounds` (committed crop when not editing, full image while editing) instead of the raw image size. `ImageContent.Clip` is set to a `RectangleGeometry` for the committed crop whenever a crop is active and the user isn't editing; cleared otherwise. Both fire when `CropOverlay.ViewBoundsChanged` is raised.

## 5. Save flow

[PostSnipViewModel.Save](../../Flicksy.PostSnip/ViewModels/PostSnipViewModel.cs):
- **Image with drawings or crop**: render `ImageSource` + all `DrawingItem.Render(dc)` calls into a `DrawingVisual` (translated by `-cropOrigin` when cropped) → `RenderTargetBitmap` sized to the crop in pixels (or full image when uncropped) → PNG via `PngBitmapEncoder`.
- **Image without drawings or crop** or **video**: copy the source file to the destination (no re-encode).
- Save dialog is shown by the window code-behind (the VM raises `SaveDialogRequested`).

## 6. Where to look for common changes

| Change request | Primary file(s) |
| --- | --- |
| Add a new drawing tool | new file in [Interaction/Tools/](../../Flicksy.Drawing/Interaction/Tools), config interface in [Interaction/Config/](../../Flicksy.Drawing/Interaction/Config), wire in [DrawingView.OnDataContextChanged](../../Flicksy.Drawing/Controls/DrawingView/DrawingView.xaml.cs) + toolbar enum in [ImageEditToolsViewModel](../../Flicksy.PostSnip/ViewModels/ImageEditToolsViewModel.cs) + button in [ImageEditToolsView.xaml](../../Flicksy.PostSnip/Controls/ImageEditToolsView.xaml). |
| Change crop UI / behavior | [CropOverlayView](../../Flicksy.PostSnip/Controls/CropOverlayView.xaml.cs) for visuals + gestures, [CropOverlayViewModel](../../Flicksy.PostSnip/ViewModels/CropOverlayViewModel.cs) for state. The save side lives in [PostSnipViewModel.SaveImageWithDrawing](../../Flicksy.PostSnip/ViewModels/PostSnipViewModel.cs). |
| Modify save format | [PostSnipViewModel.Save](../../Flicksy.PostSnip/ViewModels/PostSnipViewModel.cs) + `SaveImageWithDrawing`. |
| Change toolbar layout | [ImageEditToolsView.xaml](../../Flicksy.PostSnip/Controls/ImageEditToolsView.xaml) + [PostSnipWindow.xaml](../../Flicksy.PostSnip/PostSnipWindow.xaml). |
| Modify video transport UI | [VideoPlaybackOverlay](../../Flicksy.PostSnip/Controls/VideoPlaybackOverlay.xaml.cs) (scrub slider + keyboard step, coalesced seeks). The playback engine it drives lives in [drawing.md](drawing.md). |
