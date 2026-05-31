using System;
using FFMediaToolkit.Audio;

namespace Flicksy.Drawing.Media;

// Audio decode for FFmpegMediaDecoder (ADR 0005): a read-forward source cursor, persistent
// linear-resampler state for sample-continuous (click-free) output, and N-channel -> stereo
// remix. Relies on the shared _file / _gate / _disposed / Duration / HasAudio members declared
// in the main partial (FFmpegMediaDecoder.cs), which also carries the class summary. All
// members here run under _gate (taken by the public GetAudioSamplesAt entry point).
public sealed partial class FFmpegMediaDecoder
{
    /// <summary>Output sample rate the decoder resamples to. Set by the constructor.</summary>
    private readonly int _targetSampleRate;

    /// <summary>Source-side sample rate, read from the file header. Drives the resampler ratio.</summary>
    private int _sourceSampleRate;
    /// <summary>Source samples consumed per output sample = sourceRate / targetRate. 1.0 → passthrough.</summary>
    private double _resampleRatio = 1.0;

    // ---- Audio read cursor + resampler state (all guarded by _gate) ---------

    /// <summary>Whether the audio stream is positioned (a seek has run). False forces a seek on the next call.</summary>
    private bool _audioPositioned;
    /// <summary>Source time at which the next produced output sample begins — the value a continuous
    /// caller is expected to request next. A request far from this triggers a re-seek.</summary>
    private TimeSpan _audioCursorTime;
    /// <summary>True once the source stream is exhausted; further samples are silence until the next seek.</summary>
    private bool _audioEnded;

    /// <summary>Decoded-and-remixed leftover source samples, interleaved stereo [L,R,L,R,…].</summary>
    private float[] _srcChunk = Array.Empty<float>();
    /// <summary>Number of valid stereo frames in <see cref="_srcChunk"/>.</summary>
    private int _srcChunkLen;
    /// <summary>Read position (in stereo frames) within <see cref="_srcChunk"/>.</summary>
    private int _srcChunkPos;

    /// <summary>Whether the resampler's interpolation endpoints (<see cref="_s0L"/>…) are seeded.</summary>
    private bool _haveResampleSeed;
    private float _s0L, _s0R, _s1L, _s1R;
    /// <summary>Fractional position in [0,1) between source samples s0 and s1.</summary>
    private double _frac;

    /// <summary>
    /// Read the audio stream header into the resampler state (called from the constructor when
    /// the source has audio). Returns the audio stream's duration so the constructor can fold
    /// it into the overall <see cref="Duration"/>.
    /// </summary>
    private TimeSpan InitAudioStream()
    {
        var info = _file!.Audio.Info;
        _sourceSampleRate = info.SampleRate;
        _resampleRatio = _sourceSampleRate > 0 ? (double)_sourceSampleRate / _targetSampleRate : 1.0;
        return info.Duration;
    }

    public void GetAudioSamplesAt(TimeSpan time, Span<float> destination)
    {
        // Always start zeroed — every short-circuit path below leaves silence in place.
        destination.Clear();

        if (destination.Length == 0) return;
        if (_disposed || !HasAudio) return;
        if (time >= Duration) return;
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;

        int frames = destination.Length / 2;

        lock (_gate)
        {
            if (_file is null) return;

            // How much source time this call's output spans. Used both to detect whether
            // `time` continues the previous call (and so we can read forward) and to advance
            // the cursor afterwards.
            var callSpan = TimeSpan.FromSeconds(frames / (double)_targetSampleRate);
            var tolerance = TimeSpan.FromSeconds(callSpan.TotalSeconds * 0.5);

            bool continuous = _audioPositioned
                && time >= _audioCursorTime - tolerance
                && time <= _audioCursorTime + tolerance;

            if (!continuous)
            {
                SeekAudio(time);
            }

            for (int i = 0; i < frames; i++)
            {
                ProduceSample(out float l, out float r);
                destination[2 * i] = l;
                destination[2 * i + 1] = r;
            }

            // The cursor advances by exactly this call's output span regardless of rates:
            // `frames` output samples consume `frames * ratio` source samples = `frames /
            // targetRate` seconds of source time.
            _audioCursorTime += callSpan;
        }
    }

    /// <summary>
    /// Reposition the audio stream at <paramref name="time"/> and reset the resampler. Decodes
    /// the source frame containing <paramref name="time"/>, discards the samples before
    /// <paramref name="time"/> (frame-accurate start), and clears interpolation state so the
    /// next <see cref="ProduceSample"/> reseeds from the new position.
    /// </summary>
    private void SeekAudio(TimeSpan time)
    {
        _srcChunkLen = 0;
        _srcChunkPos = 0;
        _haveResampleSeed = false;
        _frac = 0;
        _audioEnded = false;
        _audioCursorTime = time;

        try
        {
            using var data = _file!.Audio.GetFrame(time);
            int n = CopyRemixedToSrcChunk(data);

            // Drop the head samples that precede `time` so playback starts frame-accurately
            // rather than at the decoded frame's boundary (~21ms granularity at 48k/1024).
            var framePts = _file.Audio.Position;
            if (framePts <= time && _sourceSampleRate > 0)
            {
                int skip = (int)Math.Round((time - framePts).TotalSeconds * _sourceSampleRate);
                _srcChunkPos = Math.Clamp(skip, 0, n);
            }
            _audioPositioned = true;
        }
        catch
        {
            // Seek/decode failure → silence for this call, but stay unpositioned so the next
            // call re-seeks and retries rather than getting stuck silent for the whole clip.
            _audioEnded = true;
            _audioPositioned = false;
        }
    }

    /// <summary>Produce one output stereo sample via linear interpolation over the source stream.</summary>
    private void ProduceSample(out float l, out float r)
    {
        if (_audioEnded)
        {
            l = 0;
            r = 0;
            return;
        }

        if (!_haveResampleSeed)
        {
            if (!TryReadSourceSample(out _s0L, out _s0R))
            {
                _audioEnded = true;
                l = 0;
                r = 0;
                return;
            }
            // Single-sample tail is harmless: s1 == s0 yields a flat segment.
            if (!TryReadSourceSample(out _s1L, out _s1R))
            {
                _s1L = _s0L;
                _s1R = _s0R;
            }
            _frac = 0;
            _haveResampleSeed = true;
        }

        // At ratio 1.0 the fraction stays exactly 0, so this is bit-exact passthrough.
        l = (float)(_s0L + (_s1L - _s0L) * _frac);
        r = (float)(_s0R + (_s1R - _s0R) * _frac);

        _frac += _resampleRatio;
        while (_frac >= 1.0)
        {
            _frac -= 1.0;
            _s0L = _s1L;
            _s0R = _s1R;
            if (!TryReadSourceSample(out _s1L, out _s1R))
            {
                // Hold the last sample for the remainder of this output sample's interval;
                // the next call sees _audioEnded and emits silence.
                _s1L = _s0L;
                _s1R = _s0R;
                _audioEnded = true;
            }
        }
    }

    /// <summary>
    /// Pull the next remixed stereo source sample, decoding another source frame when the
    /// current chunk is exhausted. Returns false at end of stream.
    /// </summary>
    private bool TryReadSourceSample(out float l, out float r)
    {
        if (_srcChunkPos >= _srcChunkLen && !DecodeNextSourceChunk())
        {
            l = 0;
            r = 0;
            return false;
        }

        l = _srcChunk[2 * _srcChunkPos];
        r = _srcChunk[2 * _srcChunkPos + 1];
        _srcChunkPos++;
        return true;
    }

    /// <summary>Decode the next source frame (read-forward) and remix it into <see cref="_srcChunk"/>.</summary>
    private bool DecodeNextSourceChunk()
    {
        if (_file is null) return false;
        if (!_file.Audio.TryGetNextFrame(out var data)) return false;
        try
        {
            _srcChunkLen = CopyRemixedToSrcChunk(data);
            _srcChunkPos = 0;
            return _srcChunkLen > 0;
        }
        finally
        {
            data.Dispose();
        }
    }

    /// <summary>
    /// Remix one decoded <see cref="AudioData"/> frame to interleaved stereo into
    /// <see cref="_srcChunk"/> and return the stereo-frame count. FFMediaToolkit decodes to
    /// planar float, so each channel is a separate span. Mono duplicates to both channels;
    /// &gt;2 channels take the front-left/front-right pair (channels 0 and 1).
    /// </summary>
    private int CopyRemixedToSrcChunk(AudioData data)
    {
        int n = data.NumSamples;
        int ch = data.NumChannels;
        if (n <= 0 || ch <= 0) return 0;

        if (_srcChunk.Length < n * 2) _srcChunk = new float[n * 2];

        var left = data.GetChannelData(0);
        var right = ch >= 2 ? data.GetChannelData(1) : left;

        for (int i = 0; i < n; i++)
        {
            _srcChunk[2 * i] = left[i];
            _srcChunk[2 * i + 1] = right[i];
        }
        return n;
    }
}
