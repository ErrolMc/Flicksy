# Video editor — plan index

The video editor implementation is tracked on the [GitHub issue tracker](https://github.com/ErrolMc/Flicksy/issues) as 22 numbered issues. This file is a navigable overview by phase number — the issues themselves are the source of truth.

Issues labeled [`needs-design`](https://github.com/ErrolMc/Flicksy/labels/needs-design) have `What's known` and `Open questions` sections in the body and need a grilling session before implementation. Issues without that label are fully scoped and ready to pick up.

| # | Title | Status | Blocked by |
| --- | --- | --- | --- |
| [1](https://github.com/ErrolMc/Flicksy/issues/1) | Rename Flicksy.Editor project to Flicksy.PostSnip | Ready | — |
| [2](https://github.com/ErrolMc/Flicksy/issues/2) | Extract Flicksy.Drawing shared class library | Ready | #1 |
| [3](https://github.com/ErrolMc/Flicksy/issues/3) | Scaffold Flicksy.VideoEditor project | Ready | #2 |
| [4](https://github.com/ErrolMc/Flicksy/issues/4) | Document model POCOs | Ready | #3 |
| [5](https://github.com/ErrolMc/Flicksy/issues/5) | VideoEditorWindow shell | Ready | #3 |
| [6](https://github.com/ErrolMc/Flicksy/issues/6) | Preview + Transport region | Ready | #5 |
| [7](https://github.com/ErrolMc/Flicksy/issues/7) | Timeline region | Ready | #4, #5 |
| [8](https://github.com/ErrolMc/Flicksy/issues/8) | Welcome window + entry pathways | Ready | #6, #7 |
| [9](https://github.com/ErrolMc/Flicksy/issues/9) | Media bin — import sources, drag to timeline | needs-design | #7 |
| [10](https://github.com/ErrolMc/Flicksy/issues/10) | Compositor design + scaffolding | needs-design | #4 |
| [11](https://github.com/ErrolMc/Flicksy/issues/11) | Real playback — transport drives compositor | needs-design | #6, #10 |
| [12](https://github.com/ErrolMc/Flicksy/issues/12) | Timeline editing — trim, split, move, delete | needs-design | #7, #10 |
| [13](https://github.com/ErrolMc/Flicksy/issues/13) | GraphicsClip editing — drawing tools in Preview | Ready ([ADR 0013](../adr/0013-graphicsclip-editing-one-object-per-clip.md)) | #6, #10 |
| [14](https://github.com/ErrolMc/Flicksy/issues/14) | Transitions — model, UI, render | needs-design | #10, #12 |
| [15](https://github.com/ErrolMc/Flicksy/issues/15) | Per-clip transform inspector | needs-design | #10 |
| [16](https://github.com/ErrolMc/Flicksy/issues/16) | Per-clip filters / color correction | needs-design | #10 |
| [17](https://github.com/ErrolMc/Flicksy/issues/17) | Per-clip speed control | needs-design | #10, #11 |
| [18](https://github.com/ErrolMc/Flicksy/issues/18) | Per-clip audio — volume, mute, fade | needs-design | #10 |
| [19](https://github.com/ErrolMc/Flicksy/issues/19) | Independent audio tracks | needs-design | #18 |
| [20](https://github.com/ErrolMc/Flicksy/issues/20) | Export to MP4 — resolution presets | needs-design | #10 |
| [21](https://github.com/ErrolMc/Flicksy/issues/21) | Project save/load — .flicksy JSON | needs-design | #4 |
| [22](https://github.com/ErrolMc/Flicksy/issues/22) | Background recording / webcam capture | needs-design | #8 |
