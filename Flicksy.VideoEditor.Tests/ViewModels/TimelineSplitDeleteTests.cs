using System;
using System.Linq;
using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;
// The namespace Flicksy.VideoEditor.Project and the document type Flicksy.VideoEditor.Project.Project
// share a name and the namespace is in scope, so reference the type through a distinct alias.
using ProjectModel = Flicksy.VideoEditor.Project.Project;

namespace Flicksy.VideoEditor.Tests.ViewModels;

/// <summary>
/// Headless coverage of the #12 phase-5 split + delete logic on <see cref="TimelineViewModel"/>:
/// the split source-time mapping (both speeds), property inheritance, eligibility / edge guards,
/// the <see cref="Track"/> transition helpers (reassign on split, remove on delete), and the
/// undo/redo round-trips for single split, multi-split, single delete and multi-delete. The tool
/// orchestration (<c>RazorTool</c>, key bindings) is WPF-event driven and not unit-tested; the
/// math + model mutation it relies on lives here so it's verifiable without a UI (ADR 0007).
/// PixelsPerFrame is 1.0 and source times are frame-aligned at 30fps so the assertions are exact.
/// </summary>
[TestFixture]
public class TimelineSplitDeleteTests
{
    // ---- Split: source-time mapping ----------------------------------------

    [Test]
    public void SplitClipAt_Speed1_DividesSourceRangeAtPlayhead()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);   // [10, 100), dur 90

        TimelineViewModel vm = MakeViewModel(project);
        vm.SplitClipAt(clip, frame: 40);                                                // 30 frames in → +1s source

        Assert.That(track.Clips.Count, Is.EqualTo(2));

        // Left half = the original, shrunk.
        Assert.That(clip.TimelineStart, Is.EqualTo(10));
        Assert.That(clip.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(clip.Duration, Is.EqualTo(30));

        // Right half = a new clip taking the remainder.
        var right = (MediaClip)track.Clips[1];
        Assert.That(right.TimelineStart, Is.EqualTo(40));
        Assert.That(right.SourceIn, Is.EqualTo(TimeSpan.FromSeconds(3)));
        Assert.That(right.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(right.Duration, Is.EqualTo(60));
    }

    [Test]
    public void SplitClipAt_Speed2_SourceDivideScalesBySpeed()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 8, speed: 2);   // [10, 100)

        TimelineViewModel vm = MakeViewModel(project);
        vm.SplitClipAt(clip, frame: 40);                                                // 30 frames in → +2s source at 2x

        Assert.That(clip.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(4)));
        Assert.That(clip.Duration, Is.EqualTo(30));
        var right = (MediaClip)track.Clips[1];
        Assert.That(right.SourceIn, Is.EqualTo(TimeSpan.FromSeconds(4)));
        Assert.That(right.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(8)));
        Assert.That(right.Duration, Is.EqualTo(60));
    }

    // ---- Split: property inheritance ---------------------------------------

    [Test]
    public void SplitClipAt_RightHalfInheritsOriginalProperties()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip clip = AddMediaClip(track, source, start: 10, sourceIn: 0, sourceOut: 6, speed: 2);   // [10, 100)
        clip.Volume = 0.5;
        clip.Streams = ClipStreams.Video;
        clip.Name = "Intro";
        clip.Transform.RotationDegrees = 45;

        TimelineViewModel vm = MakeViewModel(project);
        vm.SplitClipAt(clip, frame: 40);

        var right = (MediaClip)track.Clips[1];
        Assert.That(right.MediaSourceId, Is.EqualTo(source.Id));
        Assert.That(right.Speed, Is.EqualTo(2));
        Assert.That(right.Volume, Is.EqualTo(0.5));
        Assert.That(right.Streams, Is.EqualTo(ClipStreams.Video));
        Assert.That(right.Name, Is.EqualTo("Intro"));
        Assert.That(right.Framerate, Is.EqualTo(30));
        Assert.That(right.Source, Is.SameAs(source));

        // Transform is deep-copied — same values, independent instance.
        Assert.That(right.Transform.RotationDegrees, Is.EqualTo(45));
        Assert.That(right.Transform, Is.Not.SameAs(clip.Transform));
    }

    // ---- Split: eligibility / edge guards ----------------------------------

    [Test]
    public void SplitClipAt_PlayheadAtOrOutsideEdges_NoOp()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);   // [10, 100)
        TimelineViewModel vm = MakeViewModel(project);

        vm.SplitClipAt(clip, frame: 10);    // exactly the start
        vm.SplitClipAt(clip, frame: 100);   // exactly the end
        vm.SplitClipAt(clip, frame: 5);     // before the clip
        vm.SplitClipAt(clip, frame: 200);   // after the clip

        Assert.That(track.Clips.Count, Is.EqualTo(1));
        Assert.That(vm.History.CanUndo, Is.False);
    }

    [Test]
    public void SplitClipAt_LockedTrack_NoOp()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project, locked: true);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);
        TimelineViewModel vm = MakeViewModel(project);

        vm.SplitClipAt(clip, frame: 40);

        Assert.That(track.Clips.Count, Is.EqualTo(1));
        Assert.That(vm.History.CanUndo, Is.False);
    }

    // ---- Split: undo / redo -------------------------------------------------

    [Test]
    public void SplitClipAt_UndoRedo_RoundTrips()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);   // [10, 100)
        TimelineViewModel vm = MakeViewModel(project);

        vm.SplitClipAt(clip, frame: 40);
        Assert.That(track.Clips.Count, Is.EqualTo(2));

        vm.History.UndoCommand.Execute(null);
        Assert.That(track.Clips.Count, Is.EqualTo(1));
        Assert.That(clip.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(5)));               // restored
        Assert.That(clip.Duration, Is.EqualTo(90));
        Assert.That(vm.SelectedClip, Is.SameAs(clip));

        vm.History.RedoCommand.Execute(null);
        Assert.That(track.Clips.Count, Is.EqualTo(2));
        Assert.That(clip.SourceOut, Is.EqualTo(TimeSpan.FromSeconds(3)));               // re-split
    }

    // ---- Split: multi (S / scissor) ----------------------------------------

    [Test]
    public void SplitSelectedAtPlayhead_SplitsEverySelectedMediaClipPlayheadCrosses()
    {
        var project = new ProjectModel();
        Track v1 = AddTrack(project);
        Track v2 = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip a = AddMediaClip(v1, source, start: 0, sourceIn: 0, sourceOut: 5);          // [0, 150)
        MediaClip b = AddMediaClip(v2, source, start: 20, sourceIn: 0, sourceOut: 5);         // [20, 170)
        TimelineViewModel vm = MakeViewModel(project);
        vm.Transport.Playhead = 60;                                                     // strictly inside both
        vm.SetSelection(new Clip[] { a, b });

        vm.SplitSelectedAtPlayheadCommand.Execute(null);

        Assert.That(v1.Clips.Count, Is.EqualTo(2));
        Assert.That(v2.Clips.Count, Is.EqualTo(2));

        // One composite undo step reverts every split at once.
        vm.History.UndoCommand.Execute(null);
        Assert.That(v1.Clips.Count, Is.EqualTo(1));
        Assert.That(v2.Clips.Count, Is.EqualTo(1));
    }

    [Test]
    public void SplitSelectedAtPlayhead_SkipsNonMediaAndUncrossedClips()
    {
        var project = new ProjectModel();
        Track video = AddTrack(project);
        Track overlay = AddTrack(project, TrackKind.Overlay);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip media = AddMediaClip(video, source, start: 0, sourceIn: 0, sourceOut: 5);   // [0, 150)
        GraphicsClip graphics = AddGraphicsClip(overlay, start: 0, duration: 150);              // GraphicsClip — split deferred to #13
        TimelineViewModel vm = MakeViewModel(project);
        vm.Transport.Playhead = 60;
        vm.SetSelection(new Clip[] { media, graphics });

        vm.SplitSelectedAtPlayheadCommand.Execute(null);

        Assert.That(video.Clips.Count, Is.EqualTo(2));     // MediaClip split
        Assert.That(overlay.Clips.Count, Is.EqualTo(1));   // GraphicsClip untouched
    }

    // ---- Transition reassignment on split (Track helper) -------------------

    [Test]
    public void ReassignTransitionsForSplit_RightEdgeTransition_MovesToRightHalf()
    {
        var track = new Track { Kind = TrackKind.Video };
        var original = new GraphicsClip();
        var rightNeighbour = new GraphicsClip();
        var rightHalf = new GraphicsClip();
        // Transition on the original's right edge: original is the LEFT participant.
        track.Transitions.Add(new Transition { LeftClipId = original.Id, RightClipId = rightNeighbour.Id });

        track.ReassignTransitionsForSplit(original, original, rightHalf);

        Assert.That(track.Transitions[0].LeftClipId, Is.EqualTo(rightHalf.Id));
        Assert.That(track.Transitions[0].RightClipId, Is.EqualTo(rightNeighbour.Id));
    }

    [Test]
    public void ReassignTransitionsForSplit_LeftEdgeTransition_StaysWithOriginalLeftHalf()
    {
        var track = new Track { Kind = TrackKind.Video };
        var original = new GraphicsClip();
        var leftNeighbour = new GraphicsClip();
        var rightHalf = new GraphicsClip();
        // Transition on the original's left edge: original is the RIGHT participant. When the original
        // is kept as the left half this is a no-op (the boundary still belongs to the original).
        track.Transitions.Add(new Transition { LeftClipId = leftNeighbour.Id, RightClipId = original.Id });

        track.ReassignTransitionsForSplit(original, original, rightHalf);

        Assert.That(track.Transitions[0].LeftClipId, Is.EqualTo(leftNeighbour.Id));
        Assert.That(track.Transitions[0].RightClipId, Is.EqualTo(original.Id));
    }

    [Test]
    public void SplitClipAt_RightEdgeTransition_ReassignsAndUndoRestores()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip c = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);      // [10, 100)
        MediaClip d = AddMediaClip(track, source, start: 100, sourceIn: 5, sourceOut: 8);     // right neighbour
        track.Transitions.Add(new Transition { LeftClipId = c.Id, RightClipId = d.Id });

        TimelineViewModel vm = MakeViewModel(project);
        vm.SplitClipAt(c, frame: 40);

        var rightHalf = (MediaClip)track.Clips[1];                                       // C2 between C and D
        Assert.That(track.Transitions.Single().LeftClipId, Is.EqualTo(rightHalf.Id));   // moved to the right half
        Assert.That(track.Transitions.Single().RightClipId, Is.EqualTo(d.Id));

        vm.History.UndoCommand.Execute(null);
        Assert.That(track.Transitions.Single().LeftClipId, Is.EqualTo(c.Id));           // restored to the original
        Assert.That(track.Transitions.Single().RightClipId, Is.EqualTo(d.Id));
    }

    // ---- Transition removal on delete (Track helper) -----------------------

    [Test]
    public void RemoveTransitionsFor_RemovesOnlyParticipatingTransitions()
    {
        var track = new Track { Kind = TrackKind.Video };
        var a = new GraphicsClip();
        var b = new GraphicsClip();
        var c = new GraphicsClip();
        var d = new GraphicsClip();
        var ab = new Transition { LeftClipId = a.Id, RightClipId = b.Id };
        var cd = new Transition { LeftClipId = c.Id, RightClipId = d.Id };
        track.Transitions.Add(ab);
        track.Transitions.Add(cd);

        IReadOnlyList<Transition> removed = track.RemoveTransitionsFor(b);

        Assert.That(removed, Is.EquivalentTo(new[] { ab }));
        Assert.That(track.Transitions, Is.EquivalentTo(new[] { cd }));
    }

    // ---- Delete: single -----------------------------------------------------

    [Test]
    public void DeleteSelected_RemovesClip_AndUndoReinsertsAndSelects()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);
        TimelineViewModel vm = MakeViewModel(project);
        vm.SelectedClip = clip;

        vm.DeleteSelectedCommand.Execute(null);
        Assert.That(track.Clips, Is.Empty);
        Assert.That(vm.SelectedClip, Is.Null);

        vm.History.UndoCommand.Execute(null);
        Assert.That(track.Clips, Does.Contain(clip));
        Assert.That(vm.SelectedClip, Is.SameAs(clip));

        vm.History.RedoCommand.Execute(null);
        Assert.That(track.Clips, Is.Empty);
        Assert.That(vm.SelectedClip, Is.Null);
    }

    [Test]
    public void DeleteSelected_GenericOnClipBase_DeletesGraphicsClip()
    {
        var project = new ProjectModel();
        Track overlay = AddTrack(project, TrackKind.Overlay);
        GraphicsClip graphics = AddGraphicsClip(overlay, start: 0, duration: 60);
        TimelineViewModel vm = MakeViewModel(project);
        vm.SelectedClip = graphics;

        vm.DeleteSelectedCommand.Execute(null);
        Assert.That(overlay.Clips, Is.Empty);

        vm.History.UndoCommand.Execute(null);
        Assert.That(overlay.Clips, Does.Contain(graphics));
    }

    [Test]
    public void DeleteSelected_RemovesParticipatingTransition_UndoRestores()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip a = AddMediaClip(track, source, start: 0, sourceIn: 0, sourceOut: 2);       // [0, 60)
        MediaClip b = AddMediaClip(track, source, start: 60, sourceIn: 2, sourceOut: 4);      // [60, 120)
        var transition = new Transition { LeftClipId = a.Id, RightClipId = b.Id };
        track.Transitions.Add(transition);
        TimelineViewModel vm = MakeViewModel(project);
        vm.SelectedClip = a;

        vm.DeleteSelectedCommand.Execute(null);
        Assert.That(track.Clips, Is.EqualTo(new Clip[] { b }));
        Assert.That(track.Transitions, Is.Empty);

        vm.History.UndoCommand.Execute(null);
        Assert.That(track.Clips, Does.Contain(a));
        Assert.That(track.Transitions, Is.EquivalentTo(new[] { transition }));
    }

    [Test]
    public void DeleteSelected_LockedTrackClip_Skipped()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project, locked: true);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip clip = AddMediaClip(track, source, start: 10, sourceIn: 2, sourceOut: 5);
        TimelineViewModel vm = MakeViewModel(project);
        vm.SetSelection(new Clip[] { clip });

        vm.DeleteSelectedCommand.Execute(null);

        Assert.That(track.Clips, Does.Contain(clip));   // inert (ADR 0006)
        Assert.That(vm.History.CanUndo, Is.False);
    }

    // ---- Delete: multi ------------------------------------------------------

    [Test]
    public void MultiDelete_CompositeUndoRedo_RestoresAllClips()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip a = AddMediaClip(track, source, start: 0, sourceIn: 0, sourceOut: 2);       // [0, 60)
        MediaClip b = AddMediaClip(track, source, start: 100, sourceIn: 0, sourceOut: 2);     // [100, 160)
        TimelineViewModel vm = MakeViewModel(project);
        vm.SetSelection(new Clip[] { a, b });

        vm.DeleteSelectedCommand.Execute(null);
        Assert.That(track.Clips, Is.Empty);

        vm.History.UndoCommand.Execute(null);                                            // one composite step
        Assert.That(track.Clips, Is.EquivalentTo(new Clip[] { a, b }));
        Assert.That(track.Clips, Is.EqualTo(new Clip[] { a, b }));                       // re-inserted in TimelineStart order

        vm.History.RedoCommand.Execute(null);
        Assert.That(track.Clips, Is.Empty);
    }

    // ---- helpers ------------------------------------------------------------

    private static TimelineViewModel MakeViewModel(ProjectModel project) =>
        new(project, new TransportViewModel(project), new UndoManager()) { PixelsPerFrame = 1.0 };

    private static Track AddTrack(ProjectModel project, TrackKind kind = TrackKind.Video, bool locked = false)
    {
        var track = new Track { Kind = kind, Locked = locked };
        project.Tracks.Add(track);
        return track;
    }

    private static MediaSource AddSource(ProjectModel project, double durationSeconds)
    {
        var source = new MediaSource
        {
            Duration = TimeSpan.FromSeconds(durationSeconds),
            HasVideo = true,
            HasAudio = true,
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

    private static GraphicsClip AddGraphicsClip(Track track, int start, int duration)
    {
        var clip = new GraphicsClip { TimelineStart = start, DurationFrames = duration };
        track.Clips.Add(clip);
        return clip;
    }
}
