using System;
using NAudio.Wave;
using Flicksy.VideoEditor.Composition;

namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// NAudio <see cref="ISampleProvider"/> that turns the per-frame <see cref="IAudioMixer"/>
/// into the continuous, arbitrary-count sample stream the output device pulls. It mixes one
/// video frame at a time (<c>SampleRate / Framerate</c> stereo frames) and hands the device
/// whatever slice it asks for, pulling the next frame when the current one is exhausted.
/// <para>
/// The device calls <see cref="Read"/> on its own thread, so <see cref="IAudioMixer.RenderAudio"/>
/// is only ever invoked there — keeping the mixer single-call-in-flight. Seeks come from the
/// UI thread via <see cref="SeekTo"/>, which only stores a pending frame (a volatile int);
/// the audio thread applies it at the next <see cref="Read"/>, so the mixer is never touched
/// cross-thread. The provider advances its own frame counter as the audio clock (ADR 0005:
/// audio is pushed open-loop; video is timed off the system clock).
/// </para>
/// </summary>
public sealed class CompositorSampleProvider : ISampleProvider
{
    private const int NoSeek = -1;

    private readonly IAudioMixer _mixer;
    private readonly Project.Project _project;

    private float[]? _frameBuffer;
    private int _framePos;
    private int _currentFrame;
    private volatile int _pendingSeek = NoSeek;

    public CompositorSampleProvider(IAudioMixer mixer, Project.Project project, WaveFormat waveFormat)
    {
        _mixer = mixer;
        _project = project;
        WaveFormat = waveFormat;
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>
    /// Reposition audio playback to <paramref name="frame"/>. Called from the UI thread; the
    /// audio thread picks it up on its next <see cref="Read"/>. Stores only an int, so no
    /// cross-thread access to the mixer or decoder cache.
    /// </summary>
    public void SeekTo(int frame) => _pendingSeek = frame < 0 ? 0 : frame;

    public int Read(float[] buffer, int offset, int count)
    {
        int written = 0;
        while (written < count)
        {
            int pending = _pendingSeek;
            if (pending != NoSeek)
            {
                _pendingSeek = NoSeek;
                _currentFrame = pending;
                _frameBuffer = null;
                _framePos = 0;
            }

            if (_frameBuffer is null || _framePos >= _frameBuffer.Length)
            {
                _frameBuffer = _mixer.RenderAudio(_project, _currentFrame).Samples;
                _framePos = 0;
                _currentFrame++;

                // Degenerate project (framerate 0 → empty buffer). Emit silence to keep the
                // device alive rather than spin.
                if (_frameBuffer.Length == 0)
                {
                    buffer.AsSpan(offset + written, count - written).Clear();
                    return count;
                }
            }

            int chunk = Math.Min(count - written, _frameBuffer.Length - _framePos);
            // Copy via Span rather than Array.Copy: Array.Copy runs a runtime array-type
            // assignability check that throws ArrayTypeMismatchException for the buffer NAudio
            // hands us through its resampler/sample-to-wave pipeline; a typed float Span copy
            // (both are float[]) does a plain memmove with no covariance check.
            _frameBuffer.AsSpan(_framePos, chunk).CopyTo(buffer.AsSpan(offset + written, chunk));
            _framePos += chunk;
            written += chunk;
        }

        // Always return the full request (zero-padded past content end) so the device keeps
        // running until the engine stops it — a short read would end playback early.
        return count;
    }
}
