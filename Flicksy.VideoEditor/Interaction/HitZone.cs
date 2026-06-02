namespace Flicksy.VideoEditor.Interaction;

/// <summary>
/// Which part of a clip (or empty lane) a timeline pointer landed on. The
/// <see cref="TimelineToolRouter"/> maps this to a hit-zone tool: <see cref="Body"/> → Move,
/// <see cref="LeftEdge"/>/<see cref="RightEdge"/> → Trim, <see cref="None"/> → Marquee.
/// </summary>
public enum HitZone
{
    /// <summary>Empty lane space (no clip under the pointer), or a non-interactive track.</summary>
    None,

    /// <summary>The interior of a clip — the move grab zone.</summary>
    Body,

    /// <summary>A clip's left (in-point) trim edge.</summary>
    LeftEdge,

    /// <summary>A clip's right (out-point) trim edge.</summary>
    RightEdge,
}
