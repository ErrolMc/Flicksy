using System;
using System.Collections.Generic;
using Flicksy.VideoEditor.Composition;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Tests.Composition;

[TestFixture]
public class CompositionPlannerTests
{
    private const int Framerate = 30;

    // ---- IsActiveAt ---------------------------------------------------------

    [Test]
    public void IsActiveAt_StartFrame_IsActive()
    {
        MediaClip clip = MakeMediaClip(timelineStart: 10, sourceSeconds: 1);
        Assert.That(CompositionPlanner.IsActiveAt(clip, 10), Is.True);
    }

    [Test]
    public void IsActiveAt_FrameJustBeforeEnd_IsActive()
    {
        // 1s @ 30fps + speed 1.0 → 30 frames. Range [10, 40) — frame 39 is still active.
        MediaClip clip = MakeMediaClip(timelineStart: 10, sourceSeconds: 1);
        Assert.That(CompositionPlanner.IsActiveAt(clip, 39), Is.True);
    }

    [Test]
    public void IsActiveAt_EndFrame_IsNotActive()
    {
        // Half-open: TimelineStart + Duration is the first frame past the clip.
        MediaClip clip = MakeMediaClip(timelineStart: 10, sourceSeconds: 1);
        Assert.That(CompositionPlanner.IsActiveAt(clip, 40), Is.False);
    }

    [Test]
    public void IsActiveAt_FrameBeforeStart_IsNotActive()
    {
        MediaClip clip = MakeMediaClip(timelineStart: 10, sourceSeconds: 1);
        Assert.That(CompositionPlanner.IsActiveAt(clip, 9), Is.False);
    }

    [Test]
    public void IsActiveAt_ZeroDurationClip_NeverActive()
    {
        // SourceOut == SourceIn → Duration = 0. Even the start frame doesn't activate.
        MediaClip clip = MakeMediaClip(timelineStart: 10, sourceSeconds: 0);
        Assert.That(CompositionPlanner.IsActiveAt(clip, 10), Is.False);
    }

    // ---- ComputeSourceTime (speed mapping) ----------------------------------

    [Test]
    public void ComputeSourceTime_AtClipStart_EqualsSourceIn()
    {
        MediaClip clip = MakeMediaClip(timelineStart: 10, sourceSeconds: 2, sourceInSeconds: 5);
        Assert.That(CompositionPlanner.ComputeSourceTime(clip, 10, Framerate),
            Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void ComputeSourceTime_Speed1_AdvancesAtRealTime()
    {
        // 30 timeline frames @ 30fps = 1s. Speed 1 → +1s of source.
        MediaClip clip = MakeMediaClip(timelineStart: 0, sourceSeconds: 10, speed: 1.0);
        Assert.That(CompositionPlanner.ComputeSourceTime(clip, 30, Framerate),
            Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void ComputeSourceTime_Speed2_AdvancesAtDouble()
    {
        // 30 timeline frames @ 30fps = 1s elapsed. Speed 2 → +2s of source.
        MediaClip clip = MakeMediaClip(timelineStart: 0, sourceSeconds: 10, speed: 2.0);
        Assert.That(CompositionPlanner.ComputeSourceTime(clip, 30, Framerate),
            Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void ComputeSourceTime_SpeedHalf_AdvancesAtHalf()
    {
        // 30 timeline frames @ 30fps = 1s elapsed. Speed 0.5 → +0.5s of source.
        MediaClip clip = MakeMediaClip(timelineStart: 0, sourceSeconds: 10, speed: 0.5);
        Assert.That(CompositionPlanner.ComputeSourceTime(clip, 30, Framerate),
            Is.EqualTo(TimeSpan.FromSeconds(0.5)));
    }

    [Test]
    public void ComputeSourceTime_WithSourceInOffsetAndShiftedStart_AppliesBoth()
    {
        // Clip starts at frame 30. SourceIn = 5s. Query frame 90 → elapsed 60 frames = 2s.
        // Speed 1 → +2s source. Result: 5s + 2s = 7s.
        MediaClip clip = MakeMediaClip(timelineStart: 30, sourceSeconds: 10, sourceInSeconds: 5);
        Assert.That(CompositionPlanner.ComputeSourceTime(clip, 90, Framerate),
            Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [Test]
    public void ComputeSourceTime_GraphicsClip_ReturnsZero()
    {
        var clip = new GraphicsClip { TimelineStart = 0, DurationFrames = 60 };
        Assert.That(CompositionPlanner.ComputeSourceTime(clip, 30, Framerate),
            Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ComputeSourceTime_NonPositiveFramerate_Throws()
    {
        MediaClip clip = MakeMediaClip(timelineStart: 0, sourceSeconds: 1);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CompositionPlanner.ComputeSourceTime(clip, 0, 0));
    }

    // ---- PlanFrame ----------------------------------------------------------

    [Test]
    public void PlanFrame_EmptyProject_ReturnsEmpty()
    {
        var project = new Project.Project();
        Assert.That(CompositionPlanner.PlanFrame(project, 0), Is.Empty);
    }

    [Test]
    public void PlanFrame_NoActiveClipAtT_ReturnsEmpty()
    {
        Project.Project project = Project.Project.CreateEmpty();
        project.Tracks[0].Clips.Add(MakeMediaClip(timelineStart: 100, sourceSeconds: 1));
        Assert.That(CompositionPlanner.PlanFrame(project, 0), Is.Empty);
    }

    [Test]
    public void PlanFrame_ActiveMediaClip_BuildsLayerWithSpeedMappedSourceTime()
    {
        Project.Project project = Project.Project.CreateEmpty();
        MediaClip clip = MakeMediaClip(timelineStart: 0, sourceSeconds: 10, sourceInSeconds: 5, speed: 2.0);
        project.Tracks[0].Clips.Add(clip);

        IReadOnlyList<CompositionLayer> layers = CompositionPlanner.PlanFrame(project, 30);

        Assert.That(layers, Has.Count.EqualTo(1));
        Assert.That(layers[0].Clip, Is.SameAs(clip));
        Assert.That(layers[0].Track, Is.SameAs(project.Tracks[0]));
        Assert.That(layers[0].ZIndex, Is.EqualTo(0));
        // 30 frames @ 30fps = 1s elapsed, ×2 speed = +2s source, + SourceIn 5s = 7s.
        Assert.That(layers[0].SourceTime, Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [Test]
    public void PlanFrame_DisabledTrack_IsSkipped()
    {
        Project.Project project = Project.Project.CreateEmpty();
        project.Tracks[0].Disabled = true;
        project.Tracks[0].Clips.Add(MakeMediaClip(timelineStart: 0, sourceSeconds: 1));

        Assert.That(CompositionPlanner.PlanFrame(project, 0), Is.Empty);
    }

    [Test]
    public void PlanFrame_MutedTrack_StillEmitsLayer()
    {
        // Muted is the audio mix's responsibility — the planner doesn't filter it.
        // The compositor's audio pass applies the per-track skip during mixing.
        Project.Project project = Project.Project.CreateEmpty();
        Track audioTrack = project.Tracks[3];
        audioTrack.Muted = true;
        MediaClip clip = MakeMediaClip(timelineStart: 0, sourceSeconds: 1, streams: ClipStreams.Audio);
        audioTrack.Clips.Add(clip);

        IReadOnlyList<CompositionLayer> layers = CompositionPlanner.PlanFrame(project, 0);
        Assert.That(layers, Has.Count.EqualTo(1));
        Assert.That(layers[0].Clip, Is.SameAs(clip));
    }

    [Test]
    public void PlanFrame_ZOrder_TopOfListIsTopOfStack()
    {
        // Photoshop convention: the top-most track in the document (Tracks[0] = "Video 1")
        // paints LAST so it ends up on top of the visual stack. Across kinds: Video then
        // Overlay then Audio (audio has no visual z, appended for the audio mix pass).
        Project.Project project = Project.Project.CreateEmpty();
        Track video1 = project.Tracks[0]; // UI-top
        Track video2 = project.Tracks[1]; // UI-below
        Track overlay = project.Tracks[2];
        Track audio = project.Tracks[3];

        MediaClip videoClip1 = MakeMediaClip(timelineStart: 0, sourceSeconds: 1);
        MediaClip videoClip2 = MakeMediaClip(timelineStart: 0, sourceSeconds: 1);
        var overlayClip = new GraphicsClip { TimelineStart = 0, DurationFrames = 30 };
        MediaClip audioClip = MakeMediaClip(timelineStart: 0, sourceSeconds: 1, streams: ClipStreams.Audio);

        video1.Clips.Add(videoClip1);
        video2.Clips.Add(videoClip2);
        overlay.Clips.Add(overlayClip);
        audio.Clips.Add(audioClip);

        IReadOnlyList<CompositionLayer> layers = CompositionPlanner.PlanFrame(project, 0);

        Assert.That(layers, Has.Count.EqualTo(4));
        // Video 2 paints first (bottom of stack); Video 1 paints on top of it.
        Assert.That(layers[0].Clip, Is.SameAs(videoClip2));
        Assert.That(layers[1].Clip, Is.SameAs(videoClip1));
        Assert.That(layers[2].Clip, Is.SameAs(overlayClip));
        Assert.That(layers[3].Clip, Is.SameAs(audioClip));
        Assert.That(layers[0].ZIndex, Is.EqualTo(0));
        Assert.That(layers[3].ZIndex, Is.EqualTo(3));
    }

    [Test]
    public void PlanFrame_NullProject_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CompositionPlanner.PlanFrame(null!, 0));
    }

    // ---- Helpers ------------------------------------------------------------

    private static MediaClip MakeMediaClip(
        int timelineStart,
        double sourceSeconds,
        double sourceInSeconds = 0,
        double speed = 1.0,
        ClipStreams streams = ClipStreams.Both)
    {
        return new MediaClip
        {
            TimelineStart = timelineStart,
            SourceIn = TimeSpan.FromSeconds(sourceInSeconds),
            SourceOut = TimeSpan.FromSeconds(sourceInSeconds + sourceSeconds),
            Speed = speed,
            Streams = streams,
            Framerate = Framerate,
        };
    }
}
