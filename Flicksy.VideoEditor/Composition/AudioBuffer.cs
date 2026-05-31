namespace Flicksy.VideoEditor.Composition;

/// <summary>
/// Output of <c>IAudioMixer.RenderAudio</c>: one video-frame's worth of mixed audio at
/// the project's sample rate. Layout is interleaved stereo float32:
/// <c>[L0, R0, L1, R1, …]</c>. <see cref="FrameCount"/> stereo frames =
/// <c>SampleRate / Framerate</c> per call.
/// </summary>
/// <param name="Samples">Interleaved stereo float32 samples. Length is always
/// <c>FrameCount * 2</c>.</param>
/// <param name="SampleRate">Sample rate the buffer was rendered at — matches
/// <c>ProjectSettings.AudioSampleRate</c>.</param>
public sealed record AudioBuffer(float[] Samples, int SampleRate)
{
    /// <summary>Number of stereo frames in <see cref="Samples"/> (<c>Samples.Length / 2</c>).</summary>
    public int FrameCount => Samples.Length / 2;
}
