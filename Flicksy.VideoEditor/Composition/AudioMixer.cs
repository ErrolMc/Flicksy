using System;
using System.Threading;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Composition;

/// <summary>
/// <see cref="IAudioMixer"/> implementation: walks <see cref="CompositionPlanner.PlanFrame"/>,
/// keeps the audible layers (skip Overlay tracks, <c>Muted</c> tracks, video-only / broken
/// clips), scales each clip's decoded samples by <c>Volume</c>, and sums them. No Skia, no
/// WPF — just decode + scale + sum, so it runs freely on NAudio's pull thread.
/// <para>
/// Owns its own <see cref="MediaDecoderCache"/>, separate from <see cref="SkiaCompositor"/>'s,
/// so a <c>Streams=Both</c> clip's audio cursor is independent of its video cursor (ADR 0005).
/// </para>
/// </summary>
public sealed class AudioMixer : IAudioMixer
{
    // Serializes RenderAudio (NAudio render thread) against Dispose (UI thread). RenderAudio
    // has a single caller, so the lock is uncontended during playback; it exists only to keep
    // the render thread from touching the decoder cache while/after Dispose tears it down —
    // WASAPI's Stop doesn't strictly join the render thread, so a pull can race teardown.
    private readonly Lock _gate = new();
    private readonly MediaDecoderCache _decoders = new();
    private bool _disposed;

    public AudioBuffer RenderAudio(Project.Project project, int frame)
    {
        ArgumentNullException.ThrowIfNull(project);

        int sampleRate = project.Settings.AudioSampleRate;
        int framerate = project.Settings.Framerate;
        int stereoFrames = framerate > 0 ? sampleRate / framerate : 0;
        var output = new float[stereoFrames * 2];

        if (stereoFrames == 0) return new AudioBuffer(output, sampleRate);

        lock (_gate)
        {
            // A pull arriving after disposal (render thread outliving Stop) gets silence.
            if (_disposed) return new AudioBuffer(output, sampleRate);

            var layers = CompositionPlanner.PlanFrame(project, frame);
            // Scratch buffer reused across all audio-eligible layers — each clip's samples
            // get scaled by Volume and accumulated into `output`.
            var scratch = new float[stereoFrames * 2];

            foreach (var layer in layers)
            {
                if (!IsAudibleLayer(layer)) continue;
                var mediaClip = (MediaClip)layer.Clip;

                var decoder = _decoders.GetOrCreate(mediaClip, sampleRate);
                if (decoder is null || !decoder.HasAudio) continue;

                decoder.GetAudioSamplesAt(layer.SourceTime, scratch);

                float volume = (float)mediaClip.Volume;
                for (int i = 0; i < scratch.Length; i++)
                {
                    output[i] += scratch[i] * volume;
                }
            }
        }

        return new AudioBuffer(output, sampleRate);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _decoders.Dispose();
        }
    }

    private static bool IsAudibleLayer(CompositionLayer layer)
    {
        if (layer.Track.Kind == TrackKind.Overlay) return false;
        if (layer.Track.Muted) return false;
        if (layer.Clip is not MediaClip mediaClip) return false;
        if (mediaClip.Streams != ClipStreams.Audio && mediaClip.Streams != ClipStreams.Both) return false;
        if (mediaClip.IsBroken) return false;
        return true;
    }
}
