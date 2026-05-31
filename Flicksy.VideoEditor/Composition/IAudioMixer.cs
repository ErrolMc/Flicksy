using System;

namespace Flicksy.VideoEditor.Composition;

/// <summary>
/// Mixes one video-frame's worth of audio for a <c>(Project, frame)</c> input. Split out of
/// <see cref="ICompositor"/> by ADR 0005 so audio can mix on NAudio's pull thread while video
/// paints on the UI thread — two calls in flight that a single-call-in-flight compositor with
/// one decoder cache cannot serve. The mixer owns its own decoder cache, independent of the
/// compositor's.
/// <para>
/// Like <see cref="ICompositor"/>: synchronous and single-call-in-flight. In playback it is
/// driven only by the audio output device's pull thread, so all access stays on that one
/// thread.
/// </para>
/// </summary>
public interface IAudioMixer : IDisposable
{
    /// <summary>
    /// Render one video-frame's worth of mixed audio at the project's sample rate:
    /// <c>SampleRate / Framerate</c> interleaved-stereo frames per call.
    /// </summary>
    AudioBuffer RenderAudio(Project.Project project, int frame);
}
