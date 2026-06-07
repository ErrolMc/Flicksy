using System;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Composition;

/// <summary>
/// Inner decode seam the compositor pulls each video layer's frame through, decoupling
/// "where does this clip's decoded frame come from" from the paint code. The same
/// <see cref="SkiaCompositor"/> then runs two ways:
/// <list type="bullet">
///   <item><b>Synchronous</b> (export, scrub, static preview): the default
///   <see cref="DecodingFrameProvider"/> decodes on the calling (UI) thread — the canonical
///   path, unchanged from before this seam existed.</item>
///   <item><b>Prefetched</b> (playback): the off-thread decode-ahead worker serves
///   already-decoded frames so the UI thread never blocks on the codec (ADR 0009).</item>
/// </list>
/// <para>
/// Buffer lifetime mirrors <see cref="IMediaDecoder.GetVideoFrameAt"/>: the
/// <see cref="VideoFrame.Buffer"/> returned by <see cref="Acquire"/> is owned by the provider;
/// the caller MUST hand it back via <see cref="Release"/> exactly once after drawing it. A
/// provider is single-call-in-flight from the compositor's thread (ADR 0004).
/// </para>
/// </summary>
public interface IClipFrameProvider
{
    /// <summary>
    /// Return the decoded video frame for <paramref name="clip"/> at <paramref name="sourceTime"/>,
    /// or <c>null</c> when there is no frame (broken clip, past end, decode failure, or — for the
    /// prefetch provider — the frame was not in the bundle). <paramref name="decodeScale"/> is the
    /// preview-quality scale (1.0 = full); a provider may downscale the decode by it (ADR 0008).
    /// The returned buffer belongs to the provider until passed to <see cref="Release"/>.
    /// </summary>
    VideoFrame? Acquire(MediaClip clip, TimeSpan sourceTime, double decodeScale);

    /// <summary>
    /// Hand a frame previously returned by <see cref="Acquire"/> back to the provider for
    /// recycling/return. Must be called exactly once per non-null <see cref="Acquire"/> result.
    /// </summary>
    void Release(VideoFrame frame);
}
