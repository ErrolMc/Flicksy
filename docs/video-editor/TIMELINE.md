# Timeline — control layout

Visual map of the timeline region (issue #7). For the canonical per-control descriptions and file links, see [ARCHITECTURE.md §5.5](../ARCHITECTURE.md).

## On-screen layout

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ TimelineView  ◀── 2×2 grid; pinned headers in column 0 stay put on H scroll  │
│ ┌────────────┬───────────────────────────────────────────────────────────┐ ░ │
│ │  spacer    │  TimeRulerView   (RulerScroller — H-slaved to Main)      │ ░ │
│ │  120×24    │  00:00   00:02   00:04   00:06   00:08   00:10   00:12   │ ░ │
│ ├────────────┼───────────────────────────────────────────────────────────┤───│
│ │TrackHeader │  ┌── ClipView ──┐          ┌── ClipView ──┐               │ ▒ │
│ │ "Video 1"  │  │  clipA.mp4   │          │  clipB.mp4   │               │ ▒ │
│ │ [M][L][D]  │  └──────────────┘          └──────────────┘               │ ▒ │
│ ├────────────┼───────────────────────────────────────────────────────────┤ ▒ │
│ │TrackHeader │           ┌── ClipView ──┐                                │ ▒ │
│ │ "Overlay"  │           │   Graphics   │                                │ ▒ │
│ │ [M][L][D]  │           └──────────────┘                                │ ▒ │
│ ├────────────┼───────────────────────────────────────────────────────────┤ ▒ │
│ │TrackHeader │                                                           │ ▒ │
│ │ "Audio 1"  │   ┌─────── ClipView ────────┐                             │ ▒ │
│ │ [M][L][D]  │   │     soundtrack.mp3      │                             │ ▒ │
│ └────────────┴───────────────────────────────────────────────────────────┴───┘
│       ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
│       (HeadersScroller — V-slaved to Main)        (MainScroller H/V scrollbars)│
│                                                                              │
│  Empty tracks (Clips.Count == 0) collapse out of both ItemsControls.         │
│  PlayheadView (red vertical line) overlays the lanes only — anchored by the  │
│  MainScroller, so it scrolls with the lane content. Hit-test transparent.    │
└──────────────────────────────────────────────────────────────────────────────┘
```

## Control tree

```
TimelineView (UserControl)                          Controls/TimelineView.xaml
└─ Border  (PreviewMouseWheel + PreviewMouseLeftButtonDown live here so they
   │       fire over any sub-scroller — wheel pan/zoom on MainScroller, click
   │       walks up from OriginalSource to find a ClipView; if none, deselect)
   │
   └─ Grid  (2 columns × 2 rows)
      ├─ (0,0)  Border  ←── corner spacer (120×24, dark fill)
      │
      ├─ (0,1)  ScrollViewer "RulerScroller"
      │         H scrollbar hidden, V disabled; H offset slaved to MainScroller
      │         └─ TimeRulerView                    Controls/Timeline/TimeRulerView.cs
      │              • FrameworkElement, ticks + MM:SS labels via OnRender
      │              • Click / drag scrubs the playhead
      │
      ├─ (1,0)  ScrollViewer "HeadersScroller"
      │         H disabled, V scrollbar hidden; V offset slaved to MainScroller
      │         └─ ItemsControl  ItemsSource=Project.Tracks
      │            └─ TrackHeaderView (× one per Track, 120×56)
      │                  Controls/Timeline/TrackHeaderView.xaml
      │                  • Track name + [M]ute / [L]ock / [D]isable toggles (bound to Track.Muted/Locked/Disabled)
      │                  • Collapsed via DataTrigger when Clips.Count == 0
      │
      └─ (1,1)  ScrollViewer "MainScroller"
                H + V scrollbars Auto
                └─ Grid
                   ├─ ItemsControl  ItemsSource=Project.Tracks
                   │   └─ ClipsLaneView (× one per Track)
                   │         Controls/Timeline/ClipsLaneView.cs
                   │         • Canvas; positions ClipViews at TimelineStart × px/frame
                   │         • Collapsed via DataTrigger when Clips.Count == 0
                   │         └─ ClipView (× one per Clip)
                   │               Controls/Timeline/ClipView.xaml
                   │               • Typed DataTemplate per Clip subtype
                   │               • Yellow border when selected
                   │               • Click → TimelineViewModel.SelectedClip
                   │
                   └─ PlayheadView                  Controls/Timeline/PlayheadView.cs
                      • Red vertical line at Playhead × PixelsPerFrame
                      • Inside MainScroller so it scrolls with the lanes
```

## Scroll sync

`MainScroller.ScrollChanged` pushes its H/V offsets into the slaves:

```
MainScroller.HorizontalOffset  ──►  RulerScroller.HorizontalOffset
MainScroller.VerticalOffset    ──►  HeadersScroller.VerticalOffset
```

The 12px margins on `RulerScroller` (right) and `HeadersScroller` (bottom) reserve gutter space matching `MainScroller`'s scrollbars so the ruler stays aligned with the lanes horizontally and the headers stay aligned with the lanes vertically.

## Who reads/writes what

- **PixelsPerFrame** lives on `TimelineViewModel`. `TimeRulerView`, `ClipsLaneView`, and `PlayheadView` all subscribe and redraw / re-layout on change.
- **Playhead** lives on `TransportViewModel`. `PlayheadView` subscribes for redraws; the ruler's click / drag handler is the only scrub source and writes via `TimelineViewModel.SeekToFrame` / `SeekToPixel`.
- **SelectedClip** is two-way mirrored: `ClipView` click → `TimelineViewModel.SelectedClip` → `VideoEditorViewModel.SelectedClip` (drives the right rail). `ClipsLaneView` reads it back to set each child's `IsSelected`. Click on anything that isn't a `ClipView` clears it (handled by `TimelineView.OnRootPreviewMouseLeftButtonDown`).
