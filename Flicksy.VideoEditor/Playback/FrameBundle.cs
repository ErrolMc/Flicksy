using System;
using System.Collections.Generic;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Composition;

namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// One output frame's worth of pre-decoded video, produced ahead of the playhead by
/// <see cref="VideoPrefetchPump"/>. Carries the planner snapshot (<see cref="Layers"/>) so the
/// compositor renders the exact layer set the pump decoded for, never re-walking the live
/// (possibly mutating) project; and the decoded video frames (<see cref="Frames"/>), keyed by
/// <c>Clip.Id</c>, for the media layers only — <c>GraphicsClip</c>s carry no decode and are
/// painted inline by the compositor.
/// <para>
/// <see cref="Generation"/> stamps the playback epoch the bundle was produced in. The pump only
/// enqueues a bundle whose generation still matches the current one, so a seek (which bumps the
/// generation) guarantees no cross-epoch bundle ever reaches the consumer.
/// </para>
/// </summary>
public sealed class FrameBundle
{
    public FrameBundle(int frame, int generation, IReadOnlyList<CompositionLayer> layers, Dictionary<Guid, VideoFrame> frames)
    {
        Frame = frame;
        Generation = generation;
        Layers = layers;
        Frames = frames;
    }

    /// <summary>The timeline frame this bundle composites.</summary>
    public int Frame { get; }

    /// <summary>The playback epoch this bundle was produced in (see <see cref="VideoPrefetchPump"/>).</summary>
    public int Generation { get; }

    /// <summary>The planner snapshot for <see cref="Frame"/> (video + overlay + audio layers, in paint order).</summary>
    public IReadOnlyList<CompositionLayer> Layers { get; }

    /// <summary>
    /// Decoded video frames keyed by <c>Clip.Id</c> (media layers only). Each
    /// <see cref="VideoFrame.Buffer"/> is rented from <c>ArrayPool</c> and owned by the bundle until
    /// the pump recycles it.
    /// </summary>
    public Dictionary<Guid, VideoFrame> Frames { get; }
}
