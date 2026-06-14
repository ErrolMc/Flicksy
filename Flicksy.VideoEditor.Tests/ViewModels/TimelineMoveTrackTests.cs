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
/// Headless coverage of the header "Move track up / down" commands
/// (<see cref="TimelineViewModel.MoveTrackUp"/> / <see cref="TimelineViewModel.MoveTrackDown"/>) and the
/// <see cref="TimelineViewModel.CanMoveTrackUp"/> / <see cref="TimelineViewModel.CanMoveTrackDown"/>
/// predicates that grey the menu items. The key guarantee: a track only ever swaps with a same-kind
/// neighbour, so the Video → Overlay → Audio banding can never be broken — Can* is false at the top /
/// bottom of each kind's group and Move is a no-op there. Moves are single undo steps and carry the
/// track's clips along. Starts from the standard 4-track <see cref="ProjectModel.CreateEmpty"/> layout:
/// Video 1 (0), Video 2 (1), Overlay (2), Audio (3).
/// </summary>
[TestFixture]
public class TimelineMoveTrackTests
{
    [Test]
    public void CanMoveTrackUp_ReflectsSameKindNeighbourAbove()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();

        Assert.That(vm.CanMoveTrackUp(project.Tracks[0]), Is.False, "Video 1 is the first track");
        Assert.That(vm.CanMoveTrackUp(project.Tracks[1]), Is.True, "Video 2 sits below Video 1 (same kind)");
        Assert.That(vm.CanMoveTrackUp(project.Tracks[2]), Is.False, "Overlay's neighbour above is Video");
        Assert.That(vm.CanMoveTrackUp(project.Tracks[3]), Is.False, "Audio's neighbour above is Overlay");
    }

    [Test]
    public void CanMoveTrackDown_ReflectsSameKindNeighbourBelow()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();

        Assert.That(vm.CanMoveTrackDown(project.Tracks[0]), Is.True, "Video 1 sits above Video 2 (same kind)");
        Assert.That(vm.CanMoveTrackDown(project.Tracks[1]), Is.False, "Video 2's neighbour below is Overlay");
        Assert.That(vm.CanMoveTrackDown(project.Tracks[2]), Is.False, "Overlay's neighbour below is Audio");
        Assert.That(vm.CanMoveTrackDown(project.Tracks[3]), Is.False, "Audio is the last track");
    }

    [Test]
    public void MoveTrackDown_WithinKind_SwapsWithNeighbour()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        Track video1 = project.Tracks[0];
        Track video2 = project.Tracks[1];

        vm.MoveTrackDown(video1);

        Assert.That(project.Tracks[0], Is.SameAs(video2));
        Assert.That(project.Tracks[1], Is.SameAs(video1));
        // Everything below the swapped pair is undisturbed — banding intact.
        Assert.That(project.Tracks[2].Kind, Is.EqualTo(TrackKind.Overlay));
        Assert.That(project.Tracks[3].Kind, Is.EqualTo(TrackKind.Audio));
    }

    [Test]
    public void MoveTrackUp_WithinKind_SwapsWithNeighbour()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        Track video1 = project.Tracks[0];
        Track video2 = project.Tracks[1];

        vm.MoveTrackUp(video2);

        Assert.That(project.Tracks[0], Is.SameAs(video2));
        Assert.That(project.Tracks[1], Is.SameAs(video1));
    }

    [Test]
    public void MoveTrackDown_AtKindBoundary_IsNoOp()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        Track video2 = project.Tracks[1];   // below it is the Overlay track — a different kind

        vm.MoveTrackDown(video2);

        Assert.That(project.Tracks[1], Is.SameAs(video2), "order unchanged");
        Assert.That(vm.History.CanUndo, Is.False, "nothing was pushed");
    }

    [Test]
    public void MoveTrackUp_AtKindBoundary_IsNoOp()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        Track overlay = project.Tracks[2];   // above it is a Video track — a different kind

        vm.MoveTrackUp(overlay);

        Assert.That(project.Tracks[2], Is.SameAs(overlay), "order unchanged");
        Assert.That(vm.History.CanUndo, Is.False, "nothing was pushed");
    }

    [Test]
    public void MoveTrack_IsOneUndoStep_AndRedoable()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        Track video1 = project.Tracks[0];
        Track video2 = project.Tracks[1];

        vm.MoveTrackDown(video1);
        Assert.That(project.Tracks[0], Is.SameAs(video2));
        Assert.That(vm.History.CanUndo, Is.True);

        vm.History.UndoCommand.Execute(null);
        Assert.That(project.Tracks[0], Is.SameAs(video1), "original order restored");
        Assert.That(project.Tracks[1], Is.SameAs(video2));

        vm.History.RedoCommand.Execute(null);
        Assert.That(project.Tracks[0], Is.SameAs(video2), "swap re-applied");
        Assert.That(project.Tracks[1], Is.SameAs(video1));
    }

    [Test]
    public void MoveTrack_CarriesClipsWithTheTrack()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        Track video1 = project.Tracks[0];
        MediaClip clip = AddClipTo(project, video1);

        vm.MoveTrackDown(video1);

        Assert.That(project.Tracks[1], Is.SameAs(video1), "the clip-bearing track moved down a slot");
        Assert.That(video1.Clips, Has.Count.EqualTo(1));
        Assert.That(video1.Clips[0], Is.SameAs(clip), "the clip rode along inside the moved instance");
    }

    [Test]
    public void MoveTrack_AmongThreeVideoTracks_OnlySwapsAdjacent()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        vm.AddTrackCommand.Execute(TrackKind.Video);   // Video 3 lands at index 2, before Overlay
        Track video1 = project.Tracks[0];
        Track video2 = project.Tracks[1];
        Track video3 = project.Tracks[2];

        // Video 1 can only step down one slot at a time; it swaps with Video 2, not past Video 3.
        vm.MoveTrackDown(video1);

        Assert.That(project.Tracks[0], Is.SameAs(video2));
        Assert.That(project.Tracks[1], Is.SameAs(video1));
        Assert.That(project.Tracks[2], Is.SameAs(video3), "the third video track is undisturbed");
        Assert.That(project.Tracks[3].Kind, Is.EqualTo(TrackKind.Overlay));
    }

    // ---- helpers ------------------------------------------------------------

    private static (TimelineViewModel vm, ProjectModel project) Setup()
    {
        ProjectModel project = ProjectModel.CreateEmpty();
        var vm = new TimelineViewModel(project, new TransportViewModel(project), new UndoManager())
        {
            PixelsPerFrame = 1.0,
        };
        return (vm, project);
    }

    private static MediaClip AddClipTo(ProjectModel project, Track track)
    {
        var source = new MediaSource
        {
            Duration = TimeSpan.FromSeconds(20),
            HasVideo = true,
            HasAudio = true,
        };
        project.MediaSources.Add(source);

        var clip = new MediaClip
        {
            MediaSourceId = source.Id,
            Source = source,
            SourceIn = TimeSpan.Zero,
            SourceOut = source.Duration,
            Streams = ClipStreams.Both,
            Framerate = 30,
            TimelineStart = 0,
        };
        track.Clips.Add(clip);
        return clip;
    }
}
