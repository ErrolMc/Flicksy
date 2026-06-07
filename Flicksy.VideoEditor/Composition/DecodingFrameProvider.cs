using System;
using System.Buffers;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Composition;

/// <summary>
/// Synchronous <see cref="IClipFrameProvider"/>: decodes each requested frame on the calling
/// thread via a <see cref="MediaDecoderCache"/> and returns its <c>ArrayPool</c> buffer on
/// <see cref="Release"/>. This is the compositor's canonical decode path (export / scrub /
/// static preview) AND the decode primitive the off-thread <c>VideoPrefetchPump</c> (ADR 0009)
/// uses to fill its queue. Single-call-in-flight like the cache it wraps — not thread-safe
/// across concurrent callers.
/// <para>
/// The target sample rate is fixed at construction (it only matters when a decoder is first
/// opened — the cache reuses thereafter), matching the old behavior where the compositor read
/// <c>project.Settings.AudioSampleRate</c> per frame but the value never changed.
/// </para>
/// </summary>
public sealed class DecodingFrameProvider : IClipFrameProvider, IDisposable
{
    private readonly MediaDecoderCache _decoders;
    private readonly int _sampleRate;
    private readonly bool _ownsCache;

    /// <summary>Create a provider owning a fresh <see cref="MediaDecoderCache"/>.</summary>
    public DecodingFrameProvider(int sampleRate)
        : this(new MediaDecoderCache(), sampleRate, ownsCache: true)
    {
    }

    /// <summary>
    /// Create a provider over an existing cache. <paramref name="ownsCache"/> controls whether
    /// <see cref="Dispose"/> tears the cache down — false when the caller manages its lifetime.
    /// </summary>
    public DecodingFrameProvider(MediaDecoderCache decoders, int sampleRate, bool ownsCache = false)
    {
        _decoders = decoders;
        _sampleRate = sampleRate;
        _ownsCache = ownsCache;
    }

    public VideoFrame? Acquire(MediaClip clip, TimeSpan sourceTime, double decodeScale)
    {
        IMediaDecoder? decoder = _decoders.GetOrCreate(clip, _sampleRate, decodeScale);
        if (decoder is null || !decoder.HasVideo) 
            return null;

        return decoder.GetVideoFrameAt(sourceTime);
    }

    public void Release(VideoFrame frame)
    {
        // VideoFrame.Buffer is rented from ArrayPool by the decoder; return it.
        if (frame.Buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(frame.Buffer);
        }
    }

    public void Dispose()
    {
        if (_ownsCache) 
            _decoders.Dispose();
    }
}
