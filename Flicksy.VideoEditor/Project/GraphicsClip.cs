using CommunityToolkit.Mvvm.ComponentModel;
using Flicksy.Drawing.Source;

namespace Flicksy.VideoEditor.Project;

/// <summary>
/// A clip wrapping a single <see cref="DrawingItem"/> (one shape or text element — the same
/// drawing primitives used by the snip editor) rather than an external media file: one graphic
/// object per clip, the Clipchamp / Final Cut model (ADR 0013). Used for annotations, callouts,
/// title cards, etc. Settable duration lives on <see cref="DurationFrames"/> because C# disallows
/// widening a get-only abstract override with a setter — <see cref="Duration"/> reads through it.
/// </summary>
public partial class GraphicsClip : Clip
{
    // Backing for the settable Duration. Named distinctly from the abstract
    // Clip.Duration property because C# does not allow widening a get-only
    // abstract override with a setter — callers mutate via DurationFrames,
    // observers read via Duration.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Duration))]
    private int durationFrames;

    public override int Duration => DurationFrames;

    public Transform2D Transform { get; } = new();

    /// <summary>The single drawing object this clip renders (null until one is placed).</summary>
    [ObservableProperty]
    private DrawingItem? item;
}
