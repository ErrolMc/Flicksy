using System;
using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.Undo.Commands;
using Flicksy.VideoEditor.ViewModels;
// The namespace Flicksy.VideoEditor.Project and the document type Flicksy.VideoEditor.Project.Project
// share a name and the namespace is in scope, so reference the type through a distinct alias.
using ProjectModel = Flicksy.VideoEditor.Project.Project;

namespace Flicksy.VideoEditor.Tests.ViewModels;

/// <summary>
/// Headless coverage of the #12 phase-4 trim math on <see cref="TimelineViewModel.ResolveTrim"/>
/// (speed mapping on both edges, source-bound clamp, 1-frame floor, neighbour clamp, broken-clip
/// shrink-only) plus the <see cref="TrimClipCommand"/> undo round-trip. The gesture orchestration
/// (<c>TrimTool</c>) is WPF-event driven and not unit-tested; all clamping it relies on lives here
/// so it's verifiable without a UI (ADR 0007). PixelsPerFrame is forced to 1.0 (the tool's frame
/// delta is derived in the view, so it's irrelevant to the resolve math). Source times are chosen
/// frame-aligned at 30fps so the source-range assertions are exact.
/// </summary>
[TestFixture]
public class TimelineTrimTests
{
    // ---- Speed mapping: right edge -----------------------------------------

    [Test]
    public void ResolveTrim_RightEdge_Speed1_ShiftsSourceOutAndHoldsStart()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);   // [10, 100)

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: false, desiredEdgeFrame: 130);       // +30 frames

        Assert.That(r.TimelineStart, Is.EqualTo(10));                                    // left edge held
        Assert.That(r.SourceIn, Is.EqualTo(TimeSpan.FromSeconds(2)));                    // in-point held
        Assert.That(r.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(6)));                   // 5 + 30/30s
        Apply(clip, r);
        Assert.That(clip.TimelineStart + clip.Duration, Is.EqualTo(130));               // end followed the drag
    }

    [Test]
    public void ResolveTrim_RightEdge_Speed2_SourceDeltaScalesBySpeed()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 8, speed: 2);   // [10, 100)

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: false, desiredEdgeFrame: 130);       // +30 frames

        // At 2x, 30 timeline frames consume 2s of source (vs 1s at 1x).
        Assert.That(r.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(10)));
        Apply(clip, r);
        Assert.That(clip.TimelineStart + clip.Duration, Is.EqualTo(130));
    }

    // ---- Speed mapping: left edge ------------------------------------------

    [Test]
    public void ResolveTrim_LeftEdge_Speed1_SlidesStartAndSourceInHoldsEnd()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 3, sourceOut: 8);   // [10, 160)

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: true, desiredEdgeFrame: 40);         // +30 frames (shrink)

        Assert.That(r.TimelineStart, Is.EqualTo(40));
        Assert.That(r.SourceIn, Is.EqualTo(TimeSpan.FromSeconds(4)));                    // 3 + 30/30s
        Assert.That(r.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(8)));                   // out-point held
        Apply(clip, r);
        Assert.That(clip.TimelineStart + clip.Duration, Is.EqualTo(160));               // right edge held
    }

    [Test]
    public void ResolveTrim_LeftEdge_Speed2_SourceDeltaScalesBySpeed()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 3, sourceOut: 9, speed: 2);   // [10, 100)

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: true, desiredEdgeFrame: 40);         // +30 frames

        Assert.That(r.TimelineStart, Is.EqualTo(40));
        Assert.That(r.SourceIn, Is.EqualTo(TimeSpan.FromSeconds(5)));                    // 3 + 30*2/30s
        Apply(clip, r);
        Assert.That(clip.TimelineStart + clip.Duration, Is.EqualTo(100));
    }

    // ---- Source-bound clamp -------------------------------------------------

    [Test]
    public void ResolveTrim_RightEdge_ClampsToSourceDuration()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 18);  // [10, 490)

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: false, desiredEdgeFrame: 600);       // way past the source

        Assert.That(r.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(20)));                  // pinned at source end
        Apply(clip, r);
        Assert.That(clip.TimelineStart + clip.Duration, Is.EqualTo(550));               // 490 + 2s headroom
    }

    [Test]
    public void ResolveTrim_LeftEdge_ClampsToSourceStart()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 100);
        var clip = AddMediaClip(track, source, start: 100, sourceIn: 1, sourceOut: 8);  // [100, 310)

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: true, desiredEdgeFrame: 50);         // extend left past source 0

        Assert.That(r.SourceIn, Is.EqualTo(TimeSpan.Zero));                             // SourceIn floored at 0
        Assert.That(r.TimelineStart, Is.EqualTo(70));                                   // 100 - 30 frames of headroom
        Apply(clip, r);
        Assert.That(clip.TimelineStart + clip.Duration, Is.EqualTo(310));              // right edge held
    }

    // ---- 1-frame minimum ----------------------------------------------------

    [Test]
    public void ResolveTrim_RightEdge_FloorsAtOneFrame()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);   // [10, 100)

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: false, desiredEdgeFrame: 3);         // drag the end past the start

        Apply(clip, r);
        Assert.That(clip.Duration, Is.EqualTo(1));
        Assert.That(clip.TimelineStart, Is.EqualTo(10));                                // start unchanged
    }

    [Test]
    public void ResolveTrim_LeftEdge_FloorsAtOneFrame()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);   // [10, 100)

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: true, desiredEdgeFrame: 200);        // drag the start past the end

        Apply(clip, r);
        Assert.That(clip.Duration, Is.EqualTo(1));
        Assert.That(clip.TimelineStart, Is.EqualTo(99));
        Assert.That(clip.TimelineStart + clip.Duration, Is.EqualTo(100));              // right edge held
    }

    // ---- Neighbour clamp ----------------------------------------------------

    [Test]
    public void ResolveTrim_RightEdge_ClampsToNextNeighbour()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 100);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);   // [10, 100)
        AddMediaClip(track, source, start: 150, sourceIn: 2, sourceOut: 5);            // right neighbour at 150

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: false, desiredEdgeFrame: 300);       // try to extend past it

        Apply(clip, r);
        Assert.That(clip.TimelineStart + clip.Duration, Is.EqualTo(150));              // pinned at the neighbour edge
    }

    [Test]
    public void ResolveTrim_LeftEdge_ClampsToPrevNeighbour()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 100);
        AddMediaClip(track, source, start: 0, sourceIn: 0, sourceOut: 2);              // prev neighbour [0, 60)
        var clip = AddMediaClip(track, source, start: 100, sourceIn: 2, sourceOut: 5); // [100, 190)

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: true, desiredEdgeFrame: 10);         // try to extend before it

        Assert.That(r.TimelineStart, Is.EqualTo(60));                                   // pinned at the neighbour edge
        Apply(clip, r);
        Assert.That(clip.TimelineStart + clip.Duration, Is.EqualTo(190));             // right edge held
    }

    // ---- Broken clip: shrink but not extend ---------------------------------

    [Test]
    public void ResolveTrim_BrokenClip_RightEdge_ExtendRefused_ShrinkAllowed()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20, missing: true);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);   // [10, 100)
        Assert.That(clip.IsBroken, Is.True);

        var vm = MakeViewModel(project);

        // Extend refused: a missing-source clip can't grow past its current edge.
        var extend = vm.ResolveTrim(clip, fromLeftEdge: false, desiredEdgeFrame: 200);
        Assert.That(extend, Is.EqualTo(TrimResult.Capture(clip)));

        // Shrink allowed.
        var shrink = vm.ResolveTrim(clip, fromLeftEdge: false, desiredEdgeFrame: 70);
        Apply(clip, shrink);
        Assert.That(clip.Duration, Is.EqualTo(60));
    }

    [Test]
    public void ResolveTrim_BrokenClip_LeftEdge_ExtendRefused_ShrinkAllowed()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20, missing: true);
        var clip = AddMediaClip(track, source, start: 50, sourceIn: 2, sourceOut: 5);   // [50, 140)

        var vm = MakeViewModel(project);

        var extend = vm.ResolveTrim(clip, fromLeftEdge: true, desiredEdgeFrame: 10);
        Assert.That(extend, Is.EqualTo(TrimResult.Capture(clip)));

        var shrink = vm.ResolveTrim(clip, fromLeftEdge: true, desiredEdgeFrame: 80);
        Apply(clip, shrink);
        Assert.That(clip.TimelineStart, Is.EqualTo(80));
        Assert.That(clip.TimelineStart + clip.Duration, Is.EqualTo(140));             // right edge held
    }

    // ---- Frame-0 floor (distinct from the source floor) ---------------------

    [Test]
    public void ResolveTrim_LeftEdge_FloorsAtFrameZero()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 100);
        var clip = AddMediaClip(track, source, start: 20, sourceIn: 10, sourceOut: 15); // source has ample headroom

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: true, desiredEdgeFrame: -50);        // drag off the left end

        Assert.That(r.TimelineStart, Is.EqualTo(0));                                    // frame-0 binds before source
    }

    // ---- Degenerate clip: no-op --------------------------------------------

    [Test]
    public void ResolveTrim_ZeroLengthClip_NoOp()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 5, sourceOut: 5);   // Duration 0

        var vm = MakeViewModel(project);
        var r = vm.ResolveTrim(clip, fromLeftEdge: false, desiredEdgeFrame: 50);

        Assert.That(r, Is.EqualTo(TrimResult.Capture(clip)));
    }

    // ---- Command undo / redo round-trip ------------------------------------

    [Test]
    public void TrimClipCommand_UndoRedo_RestoresStateAndSelectsClip()
    {
        var project = new ProjectModel();
        var track = AddTrack(project);
        var source = AddSource(project, durationSeconds: 20);
        var clip = AddMediaClip(track, source, start: 10, sourceIn: 3, sourceOut: 8);   // [10, 160)
        var vm = MakeViewModel(project);

        var before = TrimResult.Capture(clip);
        var after = vm.ResolveTrim(clip, fromLeftEdge: true, desiredEdgeFrame: 40);
        Apply(clip, after);                                                             // the gesture mutates live
        vm.History.Push(new TrimClipCommand(vm, clip, before, after));

        vm.History.UndoCommand.Execute(null);
        Assert.That(clip.TimelineStart, Is.EqualTo(10));
        Assert.That(clip.SourceIn, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(clip.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(8)));
        Assert.That(clip.Duration, Is.EqualTo(150));
        Assert.That(vm.SelectedClip, Is.SameAs(clip));

        vm.History.RedoCommand.Execute(null);
        Assert.That(clip.TimelineStart, Is.EqualTo(40));
        Assert.That(clip.SourceIn, Is.EqualTo(TimeSpan.FromSeconds(4)));
        Assert.That(clip.Duration, Is.EqualTo(120));
    }

    // ---- helpers ------------------------------------------------------------

    private static TimelineViewModel MakeViewModel(ProjectModel project) =>
        new(project, new TransportViewModel(project), new UndoManager()) { PixelsPerFrame = 1.0 };

    private static Track AddTrack(ProjectModel project, TrackKind kind = TrackKind.Video)
    {
        var track = new Track { Kind = kind };
        project.Tracks.Add(track);
        return track;
    }

    private static MediaSource AddSource(ProjectModel project, double durationSeconds, bool missing = false)
    {
        var source = new MediaSource
        {
            Duration = TimeSpan.FromSeconds(durationSeconds),
            HasVideo = true,
            HasAudio = true,
            IsMissing = missing,
        };
        project.MediaSources.Add(source);
        return source;
    }

    private static MediaClip AddMediaClip(Track track, MediaSource source, int start,
        double sourceIn, double sourceOut, double speed = 1.0, int framerate = 30)
    {
        var clip = new MediaClip
        {
            MediaSourceId = source.Id,
            Source = source,
            SourceIn = TimeSpan.FromSeconds(sourceIn),
            SourceOut = TimeSpan.FromSeconds(sourceOut),
            Speed = speed,
            Framerate = framerate,
            TimelineStart = start,
        };
        track.Clips.Add(clip);
        return clip;
    }

    // Applies a resolved trim the way the gesture / command does, so a test can assert the
    // resulting Duration / end (the opposite-edge-held invariant), not just the raw source range.
    private static void Apply(MediaClip clip, TrimResult r)
    {
        clip.SourceIn = r.SourceIn;
        clip.SourceOut = r.SourceOut;
        clip.TimelineStart = r.TimelineStart;
    }
}
