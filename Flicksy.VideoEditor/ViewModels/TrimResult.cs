using System;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// The clamped outcome of a single-edge trim, produced by
/// <see cref="TimelineViewModel.ResolveTrim"/> and applied by the trim gesture /
/// <c>TrimClipCommand</c>. Carries the three fields a trim can move: the clip's
/// <see cref="TimelineStart"/> (changes only on a left-edge trim, where the in-point slides) and
/// the source range <see cref="SourceIn"/>/<see cref="SourceOut"/>. The opposite edge is held
/// fixed and <see cref="MediaClip.Duration"/> recomputes from the source range, so it isn't stored
/// here. Value equality (record struct) lets the gesture skip pushing an undo entry when the trim
/// resolved back to where it started.
/// </summary>
public readonly record struct TrimResult(int TimelineStart, TimeSpan SourceIn, TimeSpan SourceOut)
{
    /// <summary>Snapshots <paramref name="clip"/>'s current trim state (the gesture's before/after).</summary>
    public static TrimResult Capture(MediaClip clip) =>
        new(clip.TimelineStart, clip.SourceIn, clip.SourceOut);
}
