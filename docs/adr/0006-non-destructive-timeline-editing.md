# Non-destructive timeline editing (v1)

## Decision

#12 adds direct timeline editing (trim, split, move, delete). v1 is **non-destructive**: an edit never overlaps, shifts, or overwrites a neighbouring clip, and delete / move-away leave gaps rather than closing them. Ripple and overwrite edit modes are deferred.

**Overlap is refused, not rippled or overwritten.** When a move or trim would intersect another clip on the same track, the edit is clamped: a move relocates to the nearest free gap (the existing `TimelineViewModel.Snap` / `WalkToFreeGap` behaviour, now extended from bin drops to on-timeline moves), a trim stops at the neighbour's edge. Neighbours are never moved or cut. This upholds the existing model invariant (CONTEXT.md: "Clips on the same track do not overlap") at minimum cost.

**Delete and move-away leave gaps.** `Delete` lifts the selected clip(s) and leaves the hole; dragging a clip off its position leaves the vacated span empty. Nothing else on the track moves. Ripple-delete (close the gap) is a future command on its own shortcut.

**Trim clamps to the source; minimum 1 frame.** A trimmed edge cannot pass the media boundary (`SourceIn >= 0`, `SourceOut <= Source.Duration`), and a clip cannot be trimmed below 1 frame. No empty / black padding inside a clip. Edge drags are in timeline frames and converted to source time via `Speed`; a broken / missing-source clip can shrink but not extend.

**Cross-track move stays within a `TrackKind`.** A clip can be dragged to another track of the same kind (`Streams` preserved, never re-resolved); a drop on a different-kind track is refused. Same-track retime is `MoveClipCommand`; a track change is `MoveClipBetweenTracksCommand`.

**Transitions are kept referentially consistent now.** Per the consequence noted in [ADR 0002](0002-video-editor-document-model.md), a single `Track` helper maintains `Track.Transitions` across edits: split reassigns an outer-edge transition to the half that keeps that edge (right edge to the right half per the issue, left edge to the left half symmetrically); delete and move remove any transition the clip participates in (adjacency broken). The trim-vs-transition-duration coupling is the one piece deferred to #14, which owns the transition model. `Track.Transitions` is empty until #14 creates transitions, so this is forward-looking integrity.

## Why

- **It matches the invariant and the only code that already exists.** `Snap` / `WalkToFreeGap` already refuses overlap for bin drops; extending it to move / trim is the smallest consistent step and keeps every per-track clip list non-overlapping by construction.
- **Ripple and overwrite are cascades, not the first slice.** Ripple shifts every downstream clip (and its transitions, across tracks); overwrite must trim or delete the eaten neighbour and snapshot it for undo. Both are *modes* you toggle on in pro NLEs — additive later, not a rewrite — so leading with the predictable default loses nothing.
- **Non-destructive keeps undo simple.** No command resurrects an overwritten clip or un-cascades a ripple; each snapshots only the clip(s) the user touched.

## Considered Options

- **Ripple as the default.** Rejected for v1 — track-wide (and transition) cascade is too much surface for the first editing feature.
- **Overwrite as the default.** Rejected — destructive; undo must capture and restore eaten clips.
- **Allow empty padding when trimming past source bounds.** Rejected — needs the document model to represent partly-empty clips (new surface) for an advanced freeze-frame / slate feature.

## Consequences

- **Ripple-delete, overwrite, and ripple-trim become future opt-in modes** (their own shortcuts / a mode toggle), layered on the same commands without reworking them.
- **Gaps under the playhead composite as nothing** — already handled by `CompositionPlanner` (no active clip means transparent / silence), so no compositor change.
- **Multi-clip move is a rigid group** (same frame delta, spacing preserved, the group delta clamped against non-selected clips); **multi-trim is not supported** (trim affects only the grabbed edge). Cross-track move requires a single selection.
