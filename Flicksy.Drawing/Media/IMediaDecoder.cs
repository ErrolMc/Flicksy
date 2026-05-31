using System;

namespace Flicksy.Drawing.Media;

/// <summary>
/// Pull-shaped, single-source media decoder. Consumed by the compositor: every active
/// clip gets its own decoder instance (keyed by <c>Clip.Id</c>) so two clips of the same
/// source at different source-times each have independent cursors.
/// <para>
/// Parallel to <see cref="IVideoPlayer"/> — that interface is push-shaped (events +
/// internal clock + decode-ahead queue) and serves PostSnip's single-source playback.
/// <see cref="IMediaDecoder"/> is pull-shaped (synchronous seek + grab) and serves the
/// compositor's N-decoders-simultaneously walk per frame. PostSnip's migration onto this
/// primitive is tracked in issue #23.
/// </para>
/// <para>
/// All resampling/remixing happens inside the decoder: <see cref="GetAudioSamplesAt"/>
/// always writes interleaved stereo float32 at the decoder's configured target sample
/// rate, regardless of source format. The compositor never sees source rate or channel
/// count.
/// </para>
/// </summary>
public interface IMediaDecoder : IDisposable
{
    bool HasVideo { get; }
    bool HasAudio { get; }
    TimeSpan Duration { get; }
    int VideoWidth { get; }
    int VideoHeight { get; }

    /// <summary>
    /// Returns the video frame nearest <paramref name="time"/>, or <c>null</c> when
    /// <see cref="HasVideo"/> is false or the time is outside <see cref="Duration"/>.
    /// The returned <see cref="VideoFrame.Buffer"/> is rented from <c>ArrayPool</c>;
    /// the caller is responsible for returning it once the frame is consumed (same
    /// convention as <see cref="IVideoPlayer.FrameReady"/>).
    /// </summary>
    VideoFrame? GetVideoFrameAt(TimeSpan time);

    /// <summary>
    /// Writes <c>destination.Length / 2</c> stereo frames of audio starting at
    /// <paramref name="time"/> as interleaved float32 at the decoder's configured target
    /// sample rate. Zero-fills any region beyond <see cref="Duration"/> or when
    /// <see cref="HasAudio"/> is false. Channel layouts other than stereo are downmixed
    /// (mono duplicated to both channels; more than two channels take the
    /// front-left/front-right pair, channels 0 and 1).
    /// </summary>
    void GetAudioSamplesAt(TimeSpan time, Span<float> destination);
}
