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
/// Headless coverage of <see cref="TimelineViewModel.DetachAudio"/> — splitting a
/// <see cref="ClipStreams.Both"/> clip's audio onto a freshly-appended audio track. The detach is
/// reversible as a single composite undo step (track add + clip add + source stream flip), so the
/// key guarantees here are that one Undo unwinds all three and one Redo reinstates them. PixelsPerFrame
/// is 1.0 and starts are frame-aligned at 30fps so the assertions are exact.
/// </summary>
[TestFixture]
public class TimelineDetachAudioTests
{
    [Test]
    public void DetachAudio_AppendsAudioTrackWithAudioHalf_AndFlipsSourceToVideo()
    {
        var (vm, project, clip) = Setup();

        vm.DetachAudio(clip);

        Assert.That(project.Tracks, Has.Count.EqualTo(2));
        var audioTrack = project.Tracks[1];
        Assert.That(audioTrack.Kind, Is.EqualTo(TrackKind.Audio));
        Assert.That(audioTrack.Name, Is.EqualTo("Audio 2"));   // bare "Audio" default is never reused
        Assert.That(audioTrack.Clips, Has.Count.EqualTo(1));

        var audioHalf = (MediaClip)audioTrack.Clips[0];
        Assert.That(audioHalf.Streams, Is.EqualTo(ClipStreams.Audio));
        Assert.That(audioHalf.MediaSourceId, Is.EqualTo(clip.MediaSourceId));
        Assert.That(audioHalf.TimelineStart, Is.EqualTo(clip.TimelineStart));
        Assert.That(audioHalf.SourceIn, Is.EqualTo(clip.SourceIn));
        Assert.That(audioHalf.SourceOut, Is.EqualTo(clip.SourceOut));

        // The source keeps the video and surrenders its audio.
        Assert.That(clip.Streams, Is.EqualTo(ClipStreams.Video));
    }

    [Test]
    public void DetachAudio_IsOneUndoStep_ThatUnwindsTrackClipAndStreamFlip()
    {
        var (vm, project, clip) = Setup();

        vm.DetachAudio(clip);
        Assert.That(vm.History.CanUndo, Is.True);

        // A single Undo must reverse the whole detach — not leave the track or strip the source.
        vm.History.UndoCommand.Execute(null);

        Assert.That(project.Tracks, Has.Count.EqualTo(1));
        Assert.That(clip.Streams, Is.EqualTo(ClipStreams.Both));
        Assert.That(vm.History.CanUndo, Is.False, "the three sub-commands must collapse into one undo step");
    }

    [Test]
    public void DetachAudio_Redo_Reinstates()
    {
        var (vm, project, clip) = Setup();

        vm.DetachAudio(clip);
        vm.History.UndoCommand.Execute(null);
        vm.History.RedoCommand.Execute(null);

        Assert.That(project.Tracks, Has.Count.EqualTo(2));
        Assert.That(project.Tracks[1].Clips, Has.Count.EqualTo(1));
        Assert.That(clip.Streams, Is.EqualTo(ClipStreams.Video));
    }

    [Test]
    public void DetachAudio_OnLockedTrack_IsNoOp()
    {
        var (vm, project, clip) = Setup();
        project.Tracks[0].Locked = true;

        vm.DetachAudio(clip);

        Assert.That(project.Tracks, Has.Count.EqualTo(1));   // no track spun up
        Assert.That(clip.Streams, Is.EqualTo(ClipStreams.Both));
        Assert.That(vm.History.CanUndo, Is.False);           // nothing pushed
    }

    [Test]
    public void DetachAudio_OnAudioOnlyClip_IsNoOp()
    {
        var (vm, project, clip) = Setup();
        clip.Streams = ClipStreams.Audio;   // only Both clips can be detached

        vm.DetachAudio(clip);

        Assert.That(project.Tracks, Has.Count.EqualTo(1));
        Assert.That(vm.History.CanUndo, Is.False);
    }

    // ---- helpers ------------------------------------------------------------

    // A one-Video-track project with a single Both clip on it, ready to detach.
    private static (TimelineViewModel vm, ProjectModel project, MediaClip clip) Setup()
    {
        var project = new ProjectModel();
        var videoTrack = new Track { Kind = TrackKind.Video, Name = "Video 1" };
        project.Tracks.Add(videoTrack);

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
        videoTrack.Clips.Add(clip);

        var vm = new TimelineViewModel(project, new TransportViewModel(project), new UndoManager())
        {
            PixelsPerFrame = 1.0,
        };
        return (vm, project, clip);
    }
}
