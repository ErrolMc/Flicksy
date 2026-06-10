using System;
using System.Collections.Generic;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Playback;

/// <summary>
/// Production <see cref="IFrameBundleSource"/>: plans a frame via
/// <see cref="CompositionPlanner.PlanFrame"/> and decodes each video layer through a
/// <see cref="DecodingFrameProvider"/> (its own video <see cref="MediaDecoderCache"/>, separate
/// from the compositor's synchronous one — playback and scrub are mutually exclusive in time, so
/// the same clip is never decoded by both at once; ADR 0005's container-seek-thrash rule is
/// preserved). Skips audio-only / broken / graphics layers (graphics are painted inline by the
/// compositor on the UI thread). Total frames comes from a delegate so this stays decoupled from
/// the transport view-model.
/// </summary>
public sealed class ProjectBundleSource : IFrameBundleSource
{
    private readonly Project.Project _project;
    private readonly DecodingFrameProvider _frames;
    private readonly Func<int> _totalFrames;

    public ProjectBundleSource(Project.Project project, Func<int> totalFrames)
    {
        _project = project;
        _frames = new DecodingFrameProvider(project.Settings.AudioSampleRate, preferHardwareVideo: true);
        _totalFrames = totalFrames;
    }

    public int TotalFrames => _totalFrames();

    public FrameBundle? Produce(int frame, int generation, double decodeScale)
    {
        IReadOnlyList<CompositionLayer> layers;
        try
        {
            layers = CompositionPlanner.PlanFrame(_project, frame);
        }
        catch
        {
            // Torn read: the UI thread mutated Tracks/Clips mid-plan. Skip this frame (consumer
            // holds the previous one); the next cycle re-plans cleanly. Nothing rented yet.
            return null;
        }

        var frames = new Dictionary<Guid, VideoFrame>();
        foreach (CompositionLayer layer in layers)
        {
            if (layer.Track.Kind == TrackKind.Audio)
                continue;

            if (layer.Clip is not MediaClip clip)
                continue;   // GraphicsClip → painted inline

            if (clip.Streams == ClipStreams.Audio)
                continue;  // audio-only → no video

            if (clip.IsBroken)
                continue;

            VideoFrame? f = _frames.Acquire(clip, layer.SourceTime, decodeScale);
            if (f is not null)
                frames[clip.Id] = f.Value;
        }

        return new FrameBundle(frame, generation, layers, frames);
    }

    public void Recycle(FrameBundle bundle)
    {
        foreach (VideoFrame f in bundle.Frames.Values) 
            _frames.Release(f);

        bundle.Frames.Clear(); // guard against a double-recycle returning a buffer twice
    }

    public void Dispose() => _frames.Dispose();
}
