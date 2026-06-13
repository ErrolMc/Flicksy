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
/// Headless coverage of the corner Add-track button (<see cref="TimelineViewModel.AddTrack"/>) and the
/// header "Delete track" command (<see cref="TimelineViewModel.RemoveTrack"/> — the pure mutation; the
/// confirm dialog lives in the View so it isn't exercised here). The key guarantees: a new track lands
/// at the bottom of its kind's group with a sequential name, and both operations are single undo steps —
/// crucially, removing a track and undoing brings back the track <em>and</em> its clips (the removed
/// Track instance retains its Clips while detached). Starts from the standard 4-track
/// <see cref="ProjectModel.CreateEmpty"/> layout (Video 1, Video 2, Overlay, Audio).
/// </summary>
[TestFixture]
public class TimelineAddRemoveTrackTests
{
    [Test]
    public void AddTrack_Video_InsertsBelowVideoGroup_NamedSequentially()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();

        vm.AddTrackCommand.Execute(TrackKind.Video);

        // New Video lands after Video 2 but before Overlay, keeping the Video group contiguous.
        Assert.That(project.Tracks, Has.Count.EqualTo(5));
        Assert.That(project.Tracks[2].Kind, Is.EqualTo(TrackKind.Video));
        Assert.That(project.Tracks[2].Name, Is.EqualTo("Video 3"));
        Assert.That(project.Tracks[3].Kind, Is.EqualTo(TrackKind.Overlay));
    }

    [Test]
    public void AddTrack_Overlay_InsertsBeforeAudio_NamedFromTwo()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();

        vm.AddTrackCommand.Execute(TrackKind.Overlay);

        Track added = project.Tracks[3];
        Assert.That(added.Kind, Is.EqualTo(TrackKind.Overlay));
        Assert.That(added.Name, Is.EqualTo("Overlay 2"));
        Assert.That(project.Tracks[4].Kind, Is.EqualTo(TrackKind.Audio), "Audio stays last");
    }

    [Test]
    public void AddTrack_Audio_AppendsAtEnd_NamedFromTwo()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();

        vm.AddTrackCommand.Execute(TrackKind.Audio);

        Track added = project.Tracks[^1];
        Assert.That(added.Kind, Is.EqualTo(TrackKind.Audio));
        Assert.That(added.Name, Is.EqualTo("Audio 2"));   // bare "Audio" default is already taken
    }

    [Test]
    public void AddTrack_Audio_WhenNoAudioTrackExists_UsesBareName()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        vm.RemoveTrack(project.Tracks.First(t => t.Kind == TrackKind.Audio));   // drop the default Audio

        vm.AddTrackCommand.Execute(TrackKind.Audio);

        Assert.That(project.Tracks.Single(t => t.Kind == TrackKind.Audio).Name, Is.EqualTo("Audio"));
    }

    [Test]
    public void AddTrack_IsOneUndoStep_AndRedoable()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();

        vm.AddTrackCommand.Execute(TrackKind.Video);
        Assert.That(project.Tracks, Has.Count.EqualTo(5));
        Assert.That(vm.History.CanUndo, Is.True);

        vm.History.UndoCommand.Execute(null);
        Assert.That(project.Tracks, Has.Count.EqualTo(4));
        Assert.That(project.Tracks.Any(t => t.Name == "Video 3"), Is.False);

        vm.History.RedoCommand.Execute(null);
        Assert.That(project.Tracks, Has.Count.EqualTo(5));
        Assert.That(project.Tracks[2].Name, Is.EqualTo("Video 3"));
    }

    [Test]
    public void RemoveTrack_EmptyTrack_RemovesAndUndoRestoresAtSameIndex()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        Track overlay = project.Tracks[2];

        vm.RemoveTrack(overlay);
        Assert.That(project.Tracks, Has.Count.EqualTo(3));
        Assert.That(project.Tracks.Contains(overlay), Is.False);

        vm.History.UndoCommand.Execute(null);
        Assert.That(project.Tracks, Has.Count.EqualTo(4));
        Assert.That(project.Tracks[2], Is.SameAs(overlay), "restored at its original index");
    }

    [Test]
    public void RemoveTrack_WithClip_UndoRestoresTrackAndItsClip()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        Track videoTrack = project.Tracks[0];
        MediaClip clip = AddClipTo(project, videoTrack);

        vm.RemoveTrack(videoTrack);
        Assert.That(project.Tracks, Has.Count.EqualTo(3));

        vm.History.UndoCommand.Execute(null);
        Assert.That(project.Tracks, Has.Count.EqualTo(4));
        Assert.That(project.Tracks[0], Is.SameAs(videoTrack));
        Assert.That(videoTrack.Clips, Has.Count.EqualTo(1), "the clip returns with the restored track");
        Assert.That(videoTrack.Clips[0], Is.SameAs(clip));

        // And redo removes it again.
        vm.History.RedoCommand.Execute(null);
        Assert.That(project.Tracks, Has.Count.EqualTo(3));
    }

    [Test]
    public void RemoveTrack_DropsSelectionOfClipsOnThatTrack()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        Track videoTrack = project.Tracks[0];
        MediaClip clip = AddClipTo(project, videoTrack);
        vm.SelectedClip = clip;

        vm.RemoveTrack(videoTrack);

        Assert.That(vm.SelectedClip, Is.Null);
        Assert.That(vm.SelectedClips, Is.Empty);
    }

    [Test]
    public void RemoveTrack_KeepsSelectionOfClipsOnOtherTracks()
    {
        (TimelineViewModel vm, ProjectModel project) = Setup();
        MediaClip keep = AddClipTo(project, project.Tracks[0]);   // on Video 1
        vm.SelectedClip = keep;

        vm.RemoveTrack(project.Tracks[2]);                        // delete the (unrelated) Overlay track

        Assert.That(vm.SelectedClip, Is.SameAs(keep));
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
