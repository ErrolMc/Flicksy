using System;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// Adapts one pre-decoded <see cref="FrameBundle"/> to the compositor's
/// <see cref="IClipFrameProvider"/> seam, so a frame the scrub worker decoded off the UI thread can
/// be composited on the UI thread without re-decoding. Mirrors <see cref="VideoPrefetchPump"/>'s
/// consumer face: <see cref="Acquire"/> serves the bundle's frame for a clip; <see cref="Release"/>
/// is a no-op because the worker recycles the whole bundle (returning its rented buffers) after the
/// composite.
/// </summary>
internal sealed class BundleFrameProvider : IClipFrameProvider
{
    private readonly FrameBundle _bundle;

    public BundleFrameProvider(FrameBundle bundle) => _bundle = bundle;

    public VideoFrame? Acquire(MediaClip clip, TimeSpan sourceTime, double decodeScale)
        => _bundle.Frames.TryGetValue(clip.Id, out VideoFrame frame) ? frame : null;

    public void Release(VideoFrame frame)
    {
        // No-op: the scrub worker recycles the whole bundle after compositing it.
    }
}
