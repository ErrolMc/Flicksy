using System.Collections.Generic;
using Flicksy.Drawing.Undo;
using Flicksy.Drawing.Undo.Commands;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.Undo;
using Flicksy.VideoEditor.Undo.Commands;
using Flicksy.VideoEditor.ViewModels;
// The namespace Flicksy.VideoEditor.Project and the document type Flicksy.VideoEditor.Project.Project
// share a name and the namespace is in scope, so reference the type through a distinct alias.
using ProjectModel = Flicksy.VideoEditor.Project.Project;

namespace Flicksy.VideoEditor.Tests.ViewModels;

/// <summary>
/// Headless coverage of the #12 phase-3 move math on <see cref="TimelineViewModel"/> (snap
/// exclusion, gap clamp, rigid-group delta, cross-track guard) plus the move undo commands. The
/// gesture orchestration (<c>MoveTool</c>) is WPF-event driven and not unit-tested; all placement
/// logic it calls lives here so it's verifiable without a UI (ADR 0007). PixelsPerFrame is forced
/// to 1.0 so the 6px snap radius is a clean 6 frames.
/// </summary>
[TestFixture]
public class TimelineMoveTests
{
    // ---- Snap: exclusion of the dragged clip --------------------------------

    [Test]
    public void SnapStartEdge_ExcludesDraggedClipOwnEdges()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var dragged = AddClip(video, start: 10, duration: 10);   // [10, 20)
        var vm = MakeViewModel(project);
        vm.Transport.Playhead = 500;                             // park the playhead far away

        // Without exclusion, 12 snaps to the clip's own start (10) — 2 frames, inside the radius.
        Assert.That(vm.SnapStartEdge(12), Is.EqualTo(10));
        // Excluding the dragged clip removes its edges, so 12 stays put (nothing else in range).
        Assert.That(vm.SnapStartEdge(12, new HashSet<Clip> { dragged }), Is.EqualTo(12));
    }

    [Test]
    public void SnapStartEdge_SnapsToClipEdgeOnAnotherTrack()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var audio = AddTrack(project, TrackKind.Audio);
        var dragged = AddClip(video, 10, 10);
        AddClip(audio, 50, 10);                                  // [50, 60) on the audio lane
        var vm = MakeViewModel(project);
        vm.Transport.Playhead = 500;

        // 48 is 2 frames from the audio clip's start (50) → snaps across tracks.
        Assert.That(vm.SnapStartEdge(48, new HashSet<Clip> { dragged }), Is.EqualTo(50));
    }

    [Test]
    public void SnapStartEdge_SnapsToFrameZero()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var dragged = AddClip(video, 100, 10);
        var vm = MakeViewModel(project);
        vm.Transport.Playhead = 500;                             // not near 0

        Assert.That(vm.SnapStartEdge(3, new HashSet<Clip> { dragged }), Is.EqualTo(0));
    }

    // ---- Snap: gap walk excludes the dragged clip ---------------------------

    [Test]
    public void Snap_GapWalk_ExcludesDraggedClipFromItsOwnGap()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var dragged = AddClip(video, 50, 30);                    // [50, 80) — the only clip
        var vm = MakeViewModel(project);

        // Alt bypasses edge snap so the gap walk is isolated. Excluding the dragged clip leaves an
        // empty lane → it sits exactly where dropped.
        Assert.That(vm.Snap(55, video, 30, altHeld: true, new HashSet<Clip> { dragged }), Is.EqualTo(55));
        // Without exclusion the desired span overlaps the clip itself, so the walk shoves it to the
        // tail gap at its own end (80).
        Assert.That(vm.Snap(55, video, 30, altHeld: true), Is.EqualTo(80));
    }

    // ---- Rigid-group delta clamp --------------------------------------------

    [Test]
    public void ClampGroupDelta_FreeGroup_MovesByFullDelta_ButFloorsAtFrameZero()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var a = AddClip(video, 0, 10);
        var b = AddClip(video, 20, 10);
        var vm = MakeViewModel(project);
        var moved = new List<(Clip, int)> { (a, 0), (b, 20) };

        Assert.That(vm.ClampGroupDelta(moved, 100), Is.EqualTo(100));   // unobstructed to the right
        Assert.That(vm.ClampGroupDelta(moved, -100), Is.EqualTo(0));    // A pinned at frame 0
    }

    [Test]
    public void ClampGroupDelta_DraggingPastStaticIntoFreeSpace_FollowsTheDrag()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var a = AddClip(video, 0, 10);      // moved
        var b = AddClip(video, 30, 10);     // moved [30, 40)
        AddClip(video, 50, 10);             // static [50, 60)
        var vm = MakeViewModel(project);
        var moved = new List<(Clip, int)> { (a, 0), (b, 30) };

        // Far to the right is free, so the group jumps over the static clip instead of sticking to
        // it — matches single-clip move, letting the user drag a selection past other clips.
        Assert.That(vm.ClampGroupDelta(moved, 100), Is.EqualTo(100));
    }

    [Test]
    public void ClampGroupDelta_OverlapResolvesToNearestFittingDelta()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var a = AddClip(video, 0, 10);      // moved [0, 10)
        var b = AddClip(video, 20, 10);     // moved [20, 30) — internal gap [10, 20)
        AddClip(video, 40, 10);             // static [40, 50)
        var vm = MakeViewModel(project);
        var moved = new List<(Clip, int)> { (a, 0), (b, 20) };

        // +45 would drop A onto the static clip; the nearest fit jumps the whole group fully past
        // it (A.start lands on static.end, +50).
        Assert.That(vm.ClampGroupDelta(moved, 45), Is.EqualTo(50));
        // +35 is nearer the other fit, where the static clip nestles into the group's own gap (+30).
        Assert.That(vm.ClampGroupDelta(moved, 35), Is.EqualTo(30));
    }

    [Test]
    public void ClampGroupDelta_LeftDrag_FloorsAtFrameZeroForEarliestClip()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var a = AddClip(video, 5, 10);      // earliest moved clip
        var b = AddClip(video, 40, 10);
        var vm = MakeViewModel(project);
        var moved = new List<(Clip, int)> { (a, 5), (b, 40) };

        Assert.That(vm.ClampGroupDelta(moved, -100), Is.EqualTo(-5));   // A pinned at frame 0
    }

    // ---- Cross-track kind guard ---------------------------------------------

    [Test]
    public void CanMoveToTrack_SameKindUnlocked_Allowed()
    {
        var project = new ProjectModel();
        var v1 = AddTrack(project, TrackKind.Video);
        var v2 = AddTrack(project, TrackKind.Video);
        var clip = AddClip(v1, 0, 10);
        var vm = MakeViewModel(project);

        Assert.That(vm.CanMoveToTrack(clip, v2), Is.True);
        Assert.That(vm.CanMoveToTrack(clip, v1), Is.True);   // its own track
    }

    [Test]
    public void CanMoveToTrack_DifferentKind_Refused()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var audio = AddTrack(project, TrackKind.Audio);
        var clip = AddClip(video, 0, 10);
        var vm = MakeViewModel(project);

        Assert.That(vm.CanMoveToTrack(clip, audio), Is.False);
    }

    [Test]
    public void CanMoveToTrack_LockedTarget_Refused()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var locked = AddTrack(project, TrackKind.Video, locked: true);
        var clip = AddClip(video, 0, 10);
        var vm = MakeViewModel(project);

        Assert.That(vm.CanMoveToTrack(clip, locked), Is.False);
    }

    // ---- MoveClipToTrack sorted insert --------------------------------------

    [Test]
    public void MoveClipToTrack_InsertsInTimelineStartOrder()
    {
        var project = new ProjectModel();
        var v1 = AddTrack(project, TrackKind.Video);
        var v2 = AddTrack(project, TrackKind.Video);
        var early = AddClip(v2, 0, 10);
        var late = AddClip(v2, 100, 10);
        var moving = AddClip(v1, 0, 10);
        var vm = MakeViewModel(project);

        vm.MoveClipToTrack(moving, v2, 50);   // lands between early and late
        Assert.That(v2.Clips, Is.EqualTo(new Clip[] { early, moving, late }));
        Assert.That(v1.Clips, Does.Not.Contain(moving));
    }

    // ---- Commands: undo / redo round-trips ----------------------------------

    [Test]
    public void MoveClipCommand_UndoRedo_RestoresStartAndSelectsClip()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var clip = AddClip(video, 10, 10);
        var vm = MakeViewModel(project);

        clip.TimelineStart = 40;                          // the gesture mutates live
        vm.History.Push(new MoveClipCommand(vm, clip, before: 10, after: 40));

        vm.History.UndoCommand.Execute(null);
        Assert.That(clip.TimelineStart, Is.EqualTo(10));
        Assert.That(vm.SelectedClip, Is.SameAs(clip));

        vm.History.RedoCommand.Execute(null);
        Assert.That(clip.TimelineStart, Is.EqualTo(40));
    }

    [Test]
    public void MoveClipBetweenTracksCommand_UndoRedo_RestoresTrackAndStart()
    {
        var project = new ProjectModel();
        var v1 = AddTrack(project, TrackKind.Video);
        var v2 = AddTrack(project, TrackKind.Video);
        var clip = AddClip(v1, 10, 10);
        var vm = MakeViewModel(project);

        vm.MoveClipToTrack(clip, v2, 60);                 // live cross-track move
        vm.History.Push(new MoveClipBetweenTracksCommand(vm, clip, v1, 10, v2, 60));

        vm.History.UndoCommand.Execute(null);
        Assert.That(v1.Clips, Does.Contain(clip));
        Assert.That(v2.Clips, Does.Not.Contain(clip));
        Assert.That(clip.TimelineStart, Is.EqualTo(10));

        vm.History.RedoCommand.Execute(null);
        Assert.That(v2.Clips, Does.Contain(clip));
        Assert.That(v1.Clips, Does.Not.Contain(clip));
        Assert.That(clip.TimelineStart, Is.EqualTo(60));
    }

    [Test]
    public void MultiMove_CompositeWithSelectionScope_RestoresFullSelectionOnUndo()
    {
        var project = new ProjectModel();
        var video = AddTrack(project, TrackKind.Video);
        var a = AddClip(video, 0, 10);
        var b = AddClip(video, 50, 10);
        var vm = MakeViewModel(project);
        vm.SetSelection(new[] { a, b }, a);

        a.TimelineStart = 20;                             // live rigid-group move of +20
        b.TimelineStart = 70;
        var composite = new CompositeCommand(
            new IUndoableCommand[]
            {
                new MoveClipCommand(vm, a, 0, 20),
                new MoveClipCommand(vm, b, 50, 70),
            },
            new TimelineSelectionScope(vm));
        vm.History.Push(composite);

        vm.History.UndoCommand.Execute(null);
        Assert.That(a.TimelineStart, Is.EqualTo(0));
        Assert.That(b.TimelineStart, Is.EqualTo(50));
        // The scope restored the whole selection, not just the last child's clip.
        Assert.That(vm.SelectedClips, Is.EquivalentTo(new Clip[] { a, b }));
        Assert.That(vm.SelectedClip, Is.SameAs(a));
    }

    // ---- helpers ------------------------------------------------------------

    private static TimelineViewModel MakeViewModel(ProjectModel project)
    {
        var transport = new TransportViewModel(project);
        return new TimelineViewModel(project, transport, new UndoManager()) { PixelsPerFrame = 1.0 };
    }

    private static Track AddTrack(ProjectModel project, TrackKind kind, bool locked = false)
    {
        var track = new Track { Kind = kind, Locked = locked };
        project.Tracks.Add(track);
        return track;
    }

    private static GraphicsClip AddClip(Track track, int start, int duration)
    {
        var clip = new GraphicsClip { TimelineStart = start, DurationFrames = duration };
        track.Clips.Add(clip);
        return clip;
    }
}
