using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// The clamped outcome of a single-edge trim of a <see cref="GraphicsClip"/>, produced by
/// <see cref="TimelineViewModel.ResolveGraphicsTrim"/> and applied by the trim gesture /
/// <c>GraphicsTrimClipCommand</c>. A graphics clip has no source range — trimming is a pure
/// time-window edit — so this carries only <see cref="TimelineStart"/> (slides on a left-edge trim)
/// and <see cref="DurationFrames"/> (the mirror of <see cref="MediaClip"/>'s source-derived duration).
/// Value equality (record struct) lets the gesture skip an undo entry when the trim resolved back to
/// where it started.
/// </summary>
public readonly record struct GraphicsTrimResult(int TimelineStart, int DurationFrames)
{
    /// <summary>Snapshots <paramref name="clip"/>'s current trim state (the gesture's before/after).</summary>
    public static GraphicsTrimResult Capture(GraphicsClip clip) =>
        new(clip.TimelineStart, clip.DurationFrames);
}
