using System;
using System.Collections.Generic;
using System.Linq;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Composition;

/// <summary>
/// Stateless timeline-math service: turns a <see cref="Project"/> and a timeline frame
/// number into an ordered list of <see cref="CompositionLayer"/>s for a backend to paint
/// and mix. Zero Skia / WPF dependencies so the math (active-clip detection, speed-mapped
/// source time, track ordering) is unit-testable in isolation.
/// <para>
/// Paint order within a kind follows Photoshop's layer-panel convention: the top-most
/// track in the timeline UI (lowest index in <c>Project.Tracks</c>) paints LAST and
/// therefore ends up on top of the visual stack. Across kinds: Video first (bottom),
/// then Overlay above Video, then Audio appended at the end (no visual z but included so
/// the audio mix pass can walk the same list). <see cref="Track.Disabled"/> tracks are
/// skipped entirely. <see cref="Track.Muted"/> is NOT filtered here — it is audio-only
/// and the compositor's audio mix pass applies it.
/// </para>
/// </summary>
public static class CompositionPlanner
{
    /// <summary>
    /// Plan one frame's worth of layers, ordered for paint (lowest z first; audio layers
    /// trail).
    /// </summary>
    public static IReadOnlyList<CompositionLayer> PlanFrame(Project.Project project, int frame)
    {
        ArgumentNullException.ThrowIfNull(project);

        var layers = new List<CompositionLayer>();
        int framerate = project.Settings.Framerate;

        int z = 0;
        foreach (Track track in OrderTracksForPlanning(project.Tracks))
        {
            if (track.Disabled) 
                continue;

            foreach (Clip clip in track.Clips)
            {
                if (!IsActiveAt(clip, frame)) 
                    continue;

                layers.Add(new CompositionLayer(clip, track, z++, ComputeSourceTime(clip, frame, framerate)));
            }
        }

        return layers;
    }

    /// <summary>
    /// True when <paramref name="frame"/> falls in the clip's
    /// <c>[TimelineStart, TimelineStart + Duration)</c> half-open range. Zero-duration
    /// clips are never active.
    /// </summary>
    public static bool IsActiveAt(Clip clip, int frame)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return frame >= clip.TimelineStart && frame < clip.TimelineStart + clip.Duration;
    }

    /// <summary>
    /// Map a timeline frame to a source-side <see cref="TimeSpan"/> for a
    /// <see cref="MediaClip"/>: <c>SourceIn + ((frame - TimelineStart) / Framerate) * Speed</c>.
    /// Returns <see cref="TimeSpan.Zero"/> for clips with no source-time concept
    /// (<see cref="GraphicsClip"/> and any future time-invariant subtype).
    /// </summary>
    public static TimeSpan ComputeSourceTime(Clip clip, int frame, int framerate)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (framerate <= 0) 
            throw new ArgumentOutOfRangeException(nameof(framerate));

        if (clip is MediaClip mediaClip)
        {
            int elapsedFrames = frame - clip.TimelineStart;
            double elapsedTimelineSeconds = elapsedFrames / (double)framerate;
            double elapsedSourceSeconds = elapsedTimelineSeconds * mediaClip.Speed;
            return mediaClip.SourceIn + TimeSpan.FromSeconds(elapsedSourceSeconds);
        }
        return TimeSpan.Zero;
    }

    /// <summary>
    /// Group tracks by kind for paint ordering. Video first (lowest z), then Overlay,
    /// then Audio. Within each visual kind, **reverse** document order so the top-most
    /// track in the UI lands on top of the visual stack — Photoshop's layer-panel
    /// convention. Audio order is irrelevant for mixing and left in document order.
    /// </summary>
    private static IEnumerable<Track> OrderTracksForPlanning(IEnumerable<Track> tracks)
    {
        List<Track> list = tracks.ToList();
        IEnumerable<Track> video = list.Where(t => t.Kind == TrackKind.Video).Reverse();
        IEnumerable<Track> overlay = list.Where(t => t.Kind == TrackKind.Overlay).Reverse();
        IEnumerable<Track> audio = list.Where(t => t.Kind == TrackKind.Audio);
        return video.Concat(overlay).Concat(audio);
    }
}
