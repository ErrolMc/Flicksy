using System;
using System.Buffers;
using System.IO;
using System.Threading;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;

namespace Flicksy.Drawing.Media;

/// <summary>
/// <see cref="IMediaDecoder"/> backed by FFMediaToolkit. One instance per
/// (clip, source) pair — see <see cref="IMediaDecoder"/> for cache-key rationale.
/// <para>
/// Video reads are synchronous seek-and-grab via <c>MediaFile.Video.GetFrame</c>, the same
/// path <see cref="FFmpegVideoPlayer"/> uses for inline-on-seek presentation.
/// FFMediaToolkit's <c>GetFrame</c> reads forward without a costly seek when the requested
/// frame is the next one (or the same one), so sequential playback stays cheap; only a jump
/// triggers a real seek. A <see cref="Lock"/> serializes access because the compositor may
/// invoke multiple decoders concurrently and FFMediaToolkit's <c>MediaFile</c> is not
/// thread-safe.
/// </para>
/// <para>
/// Audio decode (per ADR 0005) lives in the <c>FFmpegMediaDecoder.Audio.cs</c> partial: a
/// read cursor reads source frames forward during playback and seeks only on a discontinuity,
/// with leftover samples + linear-resampler state persisted across calls for click-free
/// output, plus N-channel→stereo remix and rate conversion.
/// </para>
/// </summary>
public sealed partial class FFmpegMediaDecoder : IMediaDecoder
{
    private readonly Lock _gate = new();

    private MediaFile? _file;
    private bool _disposed;

    public bool HasVideo { get; private set; }
    public bool HasAudio { get; private set; }
    public TimeSpan Duration { get; private set; }
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }

    public FFmpegMediaDecoder(string path, int targetSampleRate)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        if (targetSampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(targetSampleRate));

        _targetSampleRate = targetSampleRate;

        var options = new MediaOptions
        {
            StreamsToLoad = MediaMode.AudioVideo,
            VideoPixelFormat = ImagePixelFormat.Bgra32,
        };
        _file = MediaFile.Open(Path.GetFullPath(path), options);

        HasVideo = _file.HasVideo;
        HasAudio = _file.HasAudio;

        var duration = TimeSpan.Zero;
        if (_file.HasVideo)
        {
            var info = _file.Video.Info;
            VideoWidth = info.FrameSize.Width;
            VideoHeight = info.FrameSize.Height;
            if (info.Duration > duration) duration = info.Duration;
        }
        if (_file.HasAudio)
        {
            // Audio-stream setup (source rate + resampler ratio) lives in the audio partial.
            var audioDuration = InitAudioStream();
            if (audioDuration > duration) duration = audioDuration;
        }
        Duration = duration;
    }

    public VideoFrame? GetVideoFrameAt(TimeSpan time)
    {
        if (_disposed || !HasVideo) return null;
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && time > Duration) return null;

        lock (_gate)
        {
            if (_file is null) return null;

            try
            {
                var image = _file.Video.GetFrame(time);
                var data = image.Data;
                var len = data.Length;
                var buf = ArrayPool<byte>.Shared.Rent(len);
                data.CopyTo(buf);
                return new VideoFrame(
                    buf, len,
                    image.ImageSize.Width, image.ImageSize.Height,
                    image.Stride,
                    time);
            }
            catch
            {
                // Decode failures are silent — the compositor renders black for the layer
                // and the caller treats the result as "no frame at t".
                return null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_gate)
        {
            try { _file?.Dispose(); } catch { /* ignore */ }
            _file = null;
        }
    }
}
