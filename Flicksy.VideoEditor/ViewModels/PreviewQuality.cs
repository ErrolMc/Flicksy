namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Preview render fidelity. A view-only setting (transient, per editor window) that scales the
/// compositor's target bitmap below project resolution for cheaper playback/scrubbing. It is
/// never part of the serialized project document and never affects export — only the on-screen
/// preview. See ADR 0008.
/// </summary>
public enum PreviewQuality
{
    /// <summary>Render at full project resolution (1:1).</summary>
    Full,

    /// <summary>Half the project resolution per axis (1/4 the pixels).</summary>
    Half,

    /// <summary>A quarter of the project resolution per axis (1/16 the pixels).</summary>
    Quarter,

    /// <summary>An eighth of the project resolution per axis (1/64 the pixels).</summary>
    Eighth,
}

/// <summary>Maps each <see cref="PreviewQuality"/> to its per-axis scale factor.</summary>
public static class PreviewQualityExtensions
{
    /// <summary>The per-axis multiplier applied to project resolution for this quality.</summary>
    public static double Scale(this PreviewQuality quality) => quality switch
    {
        PreviewQuality.Full => 1.0,
        PreviewQuality.Half => 0.5,
        PreviewQuality.Quarter => 0.25,
        PreviewQuality.Eighth => 0.125,
        _ => 1.0,
    };
}

/// <summary>
/// A selectable <see cref="PreviewQuality"/> paired with its display label, for the
/// preview-quality dropdown. Mirrors the label+tag shape of <see cref="RailItem"/>.
/// </summary>
public sealed class PreviewQualityOption
{
    public required string Label { get; init; }
    public required PreviewQuality Quality { get; init; }

    // The ComboBox's collapsed selection box ToStrings the selected item; returning Label
    // keeps that text correct without depending on SelectionBoxItemTemplate flowing through
    // the custom control template (which it doesn't, reliably).
    public override string ToString() => Label;
}
