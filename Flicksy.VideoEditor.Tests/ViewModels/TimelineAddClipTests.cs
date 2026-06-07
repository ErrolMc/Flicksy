using System;
using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;
// The namespace Flicksy.VideoEditor.Project and the document type Flicksy.VideoEditor.Project.Project
// share a name and the namespace is in scope, so reference the type through a distinct alias.
using ProjectModel = Flicksy.VideoEditor.Project.Project;

namespace Flicksy.VideoEditor.Tests.ViewModels;

/// <summary>
/// Headless coverage of <see cref="TimelineViewModel.AddClip"/> — the model side of a media-bin
/// drag-drop onto a lane. The WPF drop orchestration (<c>ClipsLaneView</c> payload + drop matrix +
/// snap) is event-driven and not unit-tested; the sorted insert / select / undo it delegates to lives
/// here so it's verifiable without a UI (mirrors the split + delete coverage). PixelsPerFrame is 1.0
/// and starts are frame-aligned at 30fps so the assertions are exact.
/// </summary>
[TestFixture]
public class TimelineAddClipTests
{
    [Test]
    public void AddClip_InsertsSelectsAndPushesUndo()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip clip = MakeMediaClip(source, start: 30);
        TimelineViewModel vm = MakeViewModel(project);

        vm.AddClip(track, clip);

        Assert.That(track.Clips, Does.Contain(clip));
        Assert.That(vm.SelectedClip, Is.SameAs(clip));
        Assert.That(vm.History.CanUndo, Is.True);
    }

    [Test]
    public void AddClip_Undo_RemovesAndDeselects_Redo_Reinstates()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip clip = MakeMediaClip(source, start: 30);
        TimelineViewModel vm = MakeViewModel(project);

        vm.AddClip(track, clip);

        vm.History.UndoCommand.Execute(null);
        Assert.That(track.Clips, Is.Empty);
        Assert.That(vm.SelectedClip, Is.Null);

        vm.History.RedoCommand.Execute(null);
        Assert.That(track.Clips, Does.Contain(clip));
        Assert.That(vm.SelectedClip, Is.SameAs(clip));
    }

    [Test]
    public void AddClip_InsertsInTimelineStartOrder()
    {
        var project = new ProjectModel();
        Track track = AddTrack(project);
        MediaSource source = AddSource(project, durationSeconds: 20);
        MediaClip first = MakeMediaClip(source, start: 0);
        MediaClip third = MakeMediaClip(source, start: 200);
        track.Clips.Add(first);
        track.Clips.Add(third);
        TimelineViewModel vm = MakeViewModel(project);

        MediaClip middle = MakeMediaClip(source, start: 100);
        vm.AddClip(track, middle);

        Assert.That(track.Clips, Is.EqualTo(new Clip[] { first, middle, third }));

        // Undo pulls only the dropped clip back out, leaving the pre-existing two in order.
        vm.History.UndoCommand.Execute(null);
        Assert.That(track.Clips, Is.EqualTo(new Clip[] { first, third }));
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

    private static MediaClip MakeMediaClip(MediaSource source, int start, int framerate = 30) =>
        new()
        {
            MediaSourceId = source.Id,
            Source = source,
            SourceIn = TimeSpan.Zero,
            SourceOut = source.Duration,
            Framerate = framerate,
            TimelineStart = start,
        };
}
