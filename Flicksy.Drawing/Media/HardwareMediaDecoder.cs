using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;

namespace Flicksy.Drawing.Media;

/// <summary>
/// Hardware-accelerated <see cref="IMediaDecoder"/> (video only), written directly against
/// FFmpeg.AutoGen — the same FFmpeg shared libraries FFMediaToolkit already loads — because
/// FFMediaToolkit exposes no hwaccel hooks. Device selection is CUDA-first, then D3D11VA: on
/// NVIDIA both drive the same NVDEC silicon, but d3d11va's GPU→CPU readback Maps a staging
/// texture and stalls on the whole GPU pipeline (measured 8–13 ms/frame at 1080p) where CUDA's
/// cuMemcpy costs ~1 ms; d3d11va remains the vendor-neutral path (AMD, Intel). Read-back frames
/// are converted NV12/P010 → BGRA32 by one swscale pass that also applies the optional
/// <c>targetVideoSize</c> downscale (preview quality, ADR 0008). Read-cursor semantics mirror
/// <see cref="FFmpegMediaDecoder"/>: forward reads are cheap; a jump past
/// <see cref="SeekThreshold"/> seeks back to a keyframe and decode-discards up to the target.
/// Construction throws whenever the source cannot decode on the GPU (no device, codec or profile
/// unsupported) so callers can fall back to the software decoder — see ADR 0010. Audio is never
/// decoded here (<see cref="HasAudio"/> is false); audio seams stay on
/// <see cref="FFmpegMediaDecoder"/>.
/// </summary>
public sealed unsafe class HardwareMediaDecoder : IMediaDecoder
{
    /// <summary>
    /// Kill switch. The host app sets this once at startup from its configuration (the video
    /// editor reads <c>DisableHardwareDecode</c> from appsettings.json) — this library reads no
    /// config itself, and the set-once contract keeps the repo's no-live-config convention.
    /// </summary>
    public static bool Disabled { get; set; }

    /// <summary>
    /// True when hardware decode may be attempted: <see cref="Disabled"/> is unset and at least
    /// one hardware device type could be created once for this process. Per-source failures
    /// still surface as constructor throws.
    /// </summary>
    public static bool IsAvailable => !Disabled && s_availableDeviceTypes.Value.Length > 0;

    private static readonly Lazy<AVHWDeviceType[]> s_availableDeviceTypes = new(ProbeAvailableDeviceTypes);

    // The implicit AVCodecContext_get_format_func conversion does NOT root the delegate
    // (Marshal.GetFunctionPointerForDelegate only). A static field keeps it alive for the
    // process lifetime — a collected delegate would be a native callback into freed memory.
    private static readonly AVCodecContext_get_format s_getFormat = GetFormat;

    // Mirrors FFMediaToolkit's VideoSeekThreshold so hw and sw cursors behave alike.
    private static readonly TimeSpan SeekThreshold = TimeSpan.FromMilliseconds(500);

    private readonly Lock _gate = new();
    private readonly byte*[] _srcData = new byte*[8];
    private readonly int[] _srcStride = new int[8];
    private readonly byte*[] _dstData = new byte*[4];
    private readonly int[] _dstStride = new int[4];

    private AVFormatContext* _fmt;
    private AVCodecContext* _codec;
    private AVBufferRef* _hwDevice;
    private AVPacket* _packet;
    private AVFrame* _hwFrame;   // transient decoder output (GPU surface), unref'd every cycle
    private AVFrame* _swFrame;   // persistent readback target — doubles as the last-frame cache
    private SwsContext* _sws;

    private AVHWDeviceType _deviceType;
    private int _streamIndex;
    private double _timeBaseSeconds;
    private long _streamStartTime;
    private double _fallbackFrameDurationSeconds;
    private int _dstWidth;
    private int _dstHeight;

    private bool _haveFrame;
    private TimeSpan _frameStart;
    private TimeSpan _frameEnd;
    private bool _disposed;

    public bool HasVideo => true;

    public bool HasAudio => false;

    public TimeSpan Duration { get; private set; }

    public int VideoWidth { get; private set; }

    public int VideoHeight { get; private set; }

    public HardwareMediaDecoder(string path, System.Drawing.Size? targetVideoSize = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        if (!IsAvailable)
            throw new NotSupportedException("Hardware decoding is unavailable on this machine.");

        string fullPath = Path.GetFullPath(path);
        Exception? lastFailure = null;
        foreach (AVHWDeviceType deviceType in s_availableDeviceTypes.Value)
        {
            try
            {
                Open(fullPath, deviceType, targetVideoSize);
                return;
            }
            catch (Exception exception)
            {
                // Full reset — the next candidate type (or the caller's software fallback)
                // starts from scratch.
                ReleaseNative();
                lastFailure = exception;
            }
        }

        throw lastFailure ?? new NotSupportedException("No hardware decode device accepted the source.");
    }

    public VideoFrame? GetVideoFrameAt(TimeSpan time)
    {
        if (_disposed)
            return null;

        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        if (Duration > TimeSpan.Zero && time > Duration)
            return null;

        lock (_gate)
        {
            if (_disposed)
                return null;

            try
            {
                return ProduceFrame(time);
            }
            catch (Exception exception)
            {
                // Same silent-null contract as FFmpegMediaDecoder — the layer renders nothing.
                Debug.WriteLine($"[hwdecode:{_deviceType}] decode at {time} failed: {exception.Message}");
                return null;
            }
        }
    }

    private VideoFrame? ProduceFrame(TimeSpan time)
    {
        // Repeat-frame fast path: timeline framerates above the source framerate (and paused
        // repaints) re-request the same source frame; serving the cached readback avoids a
        // keyframe seek + GOP re-decode per repeat.
        if (_haveFrame && time >= _frameStart && time < _frameEnd)
            return ConvertCurrentFrame(time);

        if (!_haveFrame || time < _frameStart || time > _frameEnd + SeekThreshold)
            Seek(time);

        if (!DecodeForward(time))
            return null;

        return ConvertCurrentFrame(time);
    }

    public void GetAudioSamplesAt(TimeSpan time, Span<float> destination) => destination.Clear();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            ReleaseNative();
        }
    }

    private void Open(string fullPath, AVHWDeviceType deviceType, System.Drawing.Size? targetVideoSize)
    {
        _deviceType = deviceType;

        AVFormatContext* fmt = null;
        Throw(ffmpeg.avformat_open_input(&fmt, fullPath, null, null), "open input");
        _fmt = fmt;

        Throw(ffmpeg.avformat_find_stream_info(_fmt, null), "find stream info");

        AVCodec* codec = null;
        _streamIndex = ffmpeg.av_find_best_stream(_fmt, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
        Throw(_streamIndex, "find video stream");

        if (!SupportsHwDevice(codec, deviceType))
            throw new NotSupportedException($"Codec {codec->id} has no {deviceType} support.");

        AVStream* stream = _fmt->streams[_streamIndex];
        for (int i = 0; i < (int)_fmt->nb_streams; i++)
        {
            if (i != _streamIndex)
                _fmt->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
        }

        if (stream->time_base.num <= 0 || stream->time_base.den <= 0)
            throw new NotSupportedException("Video stream has a degenerate time base.");

        _timeBaseSeconds = stream->time_base.num / (double)stream->time_base.den;
        _streamStartTime = stream->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : stream->start_time;

        _codec = ffmpeg.avcodec_alloc_context3(codec);
        if (_codec is null)
            throw new InvalidOperationException("Could not allocate codec context.");

        Throw(ffmpeg.avcodec_parameters_to_context(_codec, stream->codecpar), "apply codec parameters");

        AVBufferRef* device = null;
        Throw(ffmpeg.av_hwdevice_ctx_create(&device, deviceType, null, null, 0), $"create {deviceType} device");
        _hwDevice = device;

        _codec->hw_device_ctx = ffmpeg.av_buffer_ref(_hwDevice);
        _codec->get_format = s_getFormat;
        // Frame threading buys nothing under hwaccel and adds reorder latency; a single thread
        // also keeps the get_format callback on this thread.
        _codec->thread_count = 1;
        _codec->extra_hw_frames = 4;   // headroom on the fixed-size hw surface pool

        Throw(ffmpeg.avcodec_open2(_codec, codec, null), "open codec");

        VideoWidth = stream->codecpar->width;
        VideoHeight = stream->codecpar->height;
        Duration = ResolveDuration(stream);

        double framerate = 0;
        if (stream->avg_frame_rate.num > 0 && stream->avg_frame_rate.den > 0)
            framerate = stream->avg_frame_rate.num / (double)stream->avg_frame_rate.den;

        if (framerate <= 0 && stream->r_frame_rate.num > 0 && stream->r_frame_rate.den > 0)
            framerate = stream->r_frame_rate.num / (double)stream->r_frame_rate.den;

        if (framerate <= 0)
            framerate = 30;

        _fallbackFrameDurationSeconds = 1.0 / framerate;

        if (targetVideoSize is { Width: > 0, Height: > 0 } size)
        {
            _dstWidth = Math.Max(2, size.Width);
            _dstHeight = Math.Max(2, size.Height);
        }
        else
        {
            _dstWidth = Math.Max(2, VideoWidth);
            _dstHeight = Math.Max(2, VideoHeight);
        }

        _packet = ffmpeg.av_packet_alloc();
        _hwFrame = ffmpeg.av_frame_alloc();
        _swFrame = ffmpeg.av_frame_alloc();
        if (_packet is null || _hwFrame is null || _swFrame is null)
            throw new InvalidOperationException("Could not allocate decode buffers.");

        // Decode the first frame now: profile-level hwaccel refusals (which the codec-level
        // SupportsHwDevice check cannot see) must fail at construction — where the caller falls
        // back to the next device type or the software decoder — and never mid-playback.
        if (!DecodeForward(TimeSpan.Zero))
            throw new NotSupportedException("Hardware decode probe produced no frame.");
    }

    private TimeSpan ResolveDuration(AVStream* stream)
    {
        if (stream->duration > 0)
            return TimeSpan.FromSeconds(stream->duration * _timeBaseSeconds);

        // Only the container duration is in AV_TIME_BASE units; everything else in this class
        // uses the stream time base.
        if (_fmt->duration > 0)
            return TimeSpan.FromSeconds(_fmt->duration / (double)ffmpeg.AV_TIME_BASE);

        return TimeSpan.Zero;
    }

    private static bool SupportsHwDevice(AVCodec* codec, AVHWDeviceType deviceType)
    {
        for (int i = 0; ; i++)
        {
            AVCodecHWConfig* config = ffmpeg.avcodec_get_hw_config(codec, i);
            if (config is null)
                return false;

            // 0x01 = AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX (anonymous C enum — FFmpeg.AutoGen
            // does not generate it).
            if (config->device_type == deviceType && (config->methods & 0x01) != 0)
                return true;
        }
    }

    private static AVPixelFormat GetFormat(AVCodecContext* context, AVPixelFormat* formats)
    {
        for (AVPixelFormat* format = formats; *format != AVPixelFormat.AV_PIX_FMT_NONE; format++)
        {
            // Only the format matching the attached hw_device_ctx is ever offered, so accepting
            // either keeps this callback stateless across device types.
            if (*format == AVPixelFormat.AV_PIX_FMT_CUDA || *format == AVPixelFormat.AV_PIX_FMT_D3D11)
                return *format;
        }

        // Hard-fail rather than picking a software format: with thread_count = 1 an in-instance
        // software decode would crawl; failing routes the source to FFmpegMediaDecoder instead.
        // This callback must never throw across the native boundary.
        return AVPixelFormat.AV_PIX_FMT_NONE;
    }

    private static bool IsHardwareFormat(int format) =>
        format == (int)AVPixelFormat.AV_PIX_FMT_CUDA || format == (int)AVPixelFormat.AV_PIX_FMT_D3D11;

    private void Seek(TimeSpan time)
    {
        long timestamp = _streamStartTime + (long)Math.Round(time.TotalSeconds / _timeBaseSeconds);
        Throw(ffmpeg.av_seek_frame(_fmt, _streamIndex, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD), "seek");
        // Also clears any end-of-stream drain state, so seeking after playback ran off the end
        // works.
        ffmpeg.avcodec_flush_buffers(_codec);
        _haveFrame = false;
        _frameStart = TimeSpan.Zero;
        _frameEnd = TimeSpan.Zero;
    }

    /// <summary>
    /// Receive-first decode pump: drives the codec forward until <see cref="_swFrame"/> holds a
    /// frame covering <paramref name="target"/> (true), or the stream ends (the last frame is
    /// held and served for any later target — false only when nothing covers it).
    /// </summary>
    private bool DecodeForward(TimeSpan target)
    {
        while (true)
        {
            int result = ffmpeg.avcodec_receive_frame(_codec, _hwFrame);
            if (result == 0)
            {
                if (TryAcceptFrame(target))
                    return true;

                continue;
            }

            if (result == ffmpeg.AVERROR_EOF)
                return _haveFrame && target >= _frameStart;

            if (result != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                Throw(result, "receive frame");

            PumpNextPacket();
        }
    }

    private bool TryAcceptFrame(TimeSpan target)
    {
        long timestamp = _hwFrame->best_effort_timestamp;
        if (timestamp == ffmpeg.AV_NOPTS_VALUE)
            timestamp = _hwFrame->pts;

        // A fully timestampless stream is assumed contiguous with the previous frame.
        double startSeconds = timestamp == ffmpeg.AV_NOPTS_VALUE
            ? _frameEnd.TotalSeconds
            : (timestamp - _streamStartTime) * _timeBaseSeconds;
        double durationSeconds = _hwFrame->duration > 0
            ? _hwFrame->duration * _timeBaseSeconds
            : _fallbackFrameDurationSeconds;

        var start = TimeSpan.FromSeconds(startSeconds);
        var end = TimeSpan.FromSeconds(startSeconds + durationSeconds);

        if (end <= target)
        {
            // Decode-discard: a seek lands on the preceding keyframe, and the frames before the
            // target must be decoded (reference chains) but are never read back to the CPU.
            ffmpeg.av_frame_unref(_hwFrame);
            return false;
        }

        if (!IsHardwareFormat(_hwFrame->format))
        {
            ffmpeg.av_frame_unref(_hwFrame);
            throw new NotSupportedException("Decoder did not produce hardware frames for this stream.");
        }

        ffmpeg.av_frame_unref(_swFrame);
        int transferResult = ffmpeg.av_hwframe_transfer_data(_swFrame, _hwFrame, 0);
        // Always return the GPU surface to the fixed-size pool, transfer success or not.
        ffmpeg.av_frame_unref(_hwFrame);
        Throw(transferResult, "transfer hw frame");

        _frameStart = start;
        _frameEnd = end;
        _haveFrame = true;
        return true;
    }

    private void PumpNextPacket()
    {
        while (true)
        {
            int result = ffmpeg.av_read_frame(_fmt, _packet);
            if (result < 0)
            {
                // Demux end (or read error): switch the codec to drain mode. Subsequent
                // receive_frame calls flush buffered frames, then report AVERROR_EOF.
                Throw(ffmpeg.avcodec_send_packet(_codec, null), "send drain packet");
                return;
            }

            if (_packet->stream_index != _streamIndex)
            {
                ffmpeg.av_packet_unref(_packet);
                continue;
            }

            int sendResult = ffmpeg.avcodec_send_packet(_codec, _packet);
            ffmpeg.av_packet_unref(_packet);
            Throw(sendResult, "send packet");
            return;
        }
    }

    private VideoFrame ConvertCurrentFrame(TimeSpan requestedTime)
    {
        // One swscale pass does both the pixel-format conversion (NV12 or P010 — the transferred
        // frame self-describes, never assume) and the ADR 0008 decode-scale downscale. The cached
        // context recreates itself whenever the source size or format changes.
        _sws = ffmpeg.sws_getCachedContext(
            _sws,
            _swFrame->width, _swFrame->height, (AVPixelFormat)_swFrame->format,
            _dstWidth, _dstHeight, AVPixelFormat.AV_PIX_FMT_BGRA,
            ffmpeg.SWS_BILINEAR, null, null, null);
        if (_sws is null)
            throw new InvalidOperationException("Could not create swscale context.");

        for (uint i = 0; i < 8; i++)
        {
            _srcData[i] = _swFrame->data[i];
            _srcStride[i] = _swFrame->linesize[i];
        }

        int stride = _dstWidth * 4;
        int length = stride * _dstHeight;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            fixed (byte* destination = buffer)
            {
                _dstData[0] = destination;
                _dstStride[0] = stride;
                Throw(ffmpeg.sws_scale(_sws, _srcData, _srcStride, 0, _swFrame->height, _dstData, _dstStride), "convert frame");
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        return new VideoFrame(buffer, length, _dstWidth, _dstHeight, stride, requestedTime);
    }

    private void ReleaseNative()
    {
        // Fields are copied to locals because taking the address of a movable (heap) field
        // requires fixed; locals are stack-pinned by nature.
        if (_sws is not null)
        {
            ffmpeg.sws_freeContext(_sws);
            _sws = null;
        }

        if (_swFrame is not null)
        {
            AVFrame* frame = _swFrame;
            ffmpeg.av_frame_free(&frame);
            _swFrame = null;
        }

        if (_hwFrame is not null)
        {
            AVFrame* frame = _hwFrame;
            ffmpeg.av_frame_free(&frame);
            _hwFrame = null;
        }

        if (_packet is not null)
        {
            AVPacket* packet = _packet;
            ffmpeg.av_packet_free(&packet);
            _packet = null;
        }

        if (_codec is not null)
        {
            // Frees the codec's own refs on the hw device and its frames context.
            AVCodecContext* codec = _codec;
            ffmpeg.avcodec_free_context(&codec);
            _codec = null;
        }

        if (_hwDevice is not null)
        {
            AVBufferRef* device = _hwDevice;
            ffmpeg.av_buffer_unref(&device);
            _hwDevice = null;
        }

        if (_fmt is not null)
        {
            AVFormatContext* fmt = _fmt;
            ffmpeg.avformat_close_input(&fmt);
            _fmt = null;
        }

        _haveFrame = false;
        _frameStart = TimeSpan.Zero;
        _frameEnd = TimeSpan.Zero;
    }

    private static AVHWDeviceType[] ProbeAvailableDeviceTypes()
    {
        // CUDA before D3D11VA: on NVIDIA both decode on the same NVDEC silicon, but CUDA's
        // GPU->CPU readback is a plain cuMemcpy while d3d11va Maps a staging texture and stalls
        // on the GPU pipeline (~10x slower at 1080p). D3D11VA covers AMD/Intel.
        AVHWDeviceType[] candidates =
        [
            AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
        ];

        var available = new List<AVHWDeviceType>(candidates.Length);
        foreach (AVHWDeviceType type in candidates)
        {
            AVBufferRef* device = null;
            int result = ffmpeg.av_hwdevice_ctx_create(&device, type, null, null, 0);
            if (result < 0)
            {
                Debug.WriteLine($"[hwdecode] {type} unavailable: {ErrorText(result)}");
                continue;
            }

            ffmpeg.av_buffer_unref(&device);
            available.Add(type);
        }

        return available.ToArray();
    }

    private static void Throw(int result, string operation)
    {
        if (result < 0)
            throw new InvalidOperationException($"FFmpeg {operation} failed: {ErrorText(result)}");
    }

    private static string ErrorText(int error)
    {
        byte* buffer = stackalloc byte[256];
        if (ffmpeg.av_strerror(error, buffer, 256) < 0)
            return $"error {error}";

        return Marshal.PtrToStringUTF8((IntPtr)buffer) ?? $"error {error}";
    }
}
