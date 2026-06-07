using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Flicksy.VideoEditor.Project;

/// <summary>
/// Root of the video editor's document model — what a future .flicksy file will serialize
/// to. Holds project-wide <see cref="Settings"/>, the <see cref="MediaSources"/>
/// imported into the bin (referenced by id from <see cref="MediaClip"/>s), and the ordered
/// collection of <see cref="Tracks"/>. Use <see cref="CreateEmpty"/> for a fresh project
/// with defaults and a standard 4-track layout, <see cref="CreateFromSourceFile"/> to start
/// from a single media file, or <see cref="CreateStub"/> for an in-memory project
/// pre-populated with sample clips for layout/render-pipeline development.
/// </summary>
public partial class Project : ObservableObject
{
    public ProjectSettings Settings { get; } = new();

    public ObservableCollection<MediaSource> MediaSources { get; } = new();

    public ObservableCollection<Track> Tracks { get; } = new();

    public static Project CreateEmpty()
    {
        var project = new Project();
        project.Tracks.Add(new Track { Kind = TrackKind.Video, Name = "Video 1" });
        project.Tracks.Add(new Track { Kind = TrackKind.Video, Name = "Video 2" });
        project.Tracks.Add(new Track { Kind = TrackKind.Overlay, Name = "Overlay" });
        project.Tracks.Add(new Track { Kind = TrackKind.Audio, Name = "Audio" });
        return project;
    }

    public static Project CreateFromSourceFile(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path is required.", nameof(sourcePath));
        }

        Project project = CreateEmpty();
        var source = MediaSource.Probe(sourcePath);
        source = MediaSource.Probe(sourcePath);
        project.MediaSources.Add(source);

        Track videoTrack = project.Tracks[0];
        videoTrack.Clips.Add(new MediaClip
        {
            MediaSourceId = source.Id,
            Source = source,
            SourceIn = TimeSpan.Zero,
            SourceOut = source.Duration,
            Streams = ClipStreams.Both,
            Framerate = project.Settings.Framerate,
            TimelineStart = 0,
        });
        return project;
    }

    public static Project CreateStub()
    {
        Project project = CreateEmpty();
        int framerate = project.Settings.Framerate;

        // Fake-path sources with IsMissing=true so they don't probe — every stub
        // bin entry and clip will render red once 1b / step 2 land, intentionally
        // exercising the missing-source UI from the moment it exists.
        var clipA = new MediaSource
        {
            SourcePath = @"C:\fake\clipA.mp4",
            DisplayName = "clipA",
            Duration = TimeSpan.FromSeconds(3),
            HasVideo = true,
            HasAudio = true,
            Width = 1920,
            Height = 1080,
            SourceFramerate = 30,
            SampleRate = 48000,
            ChannelCount = 2,
            IsMissing = true,
        };
        var clipB = new MediaSource
        {
            SourcePath = @"C:\fake\clipB.mp4",
            DisplayName = "clipB",
            Duration = TimeSpan.FromSeconds(2),
            HasVideo = true,
            HasAudio = true,
            Width = 1920,
            Height = 1080,
            SourceFramerate = 30,
            SampleRate = 48000,
            ChannelCount = 2,
            IsMissing = true,
        };
        var soundtrack = new MediaSource
        {
            SourcePath = @"C:\fake\soundtrack.mp3",
            DisplayName = "soundtrack",
            Duration = TimeSpan.FromSeconds(5),
            HasAudio = true,
            SampleRate = 44100,
            ChannelCount = 2,
            IsMissing = true,
        };

        project.MediaSources.Add(clipA);
        project.MediaSources.Add(clipB);
        project.MediaSources.Add(soundtrack);

        Track videoTrack = project.Tracks[0];
        videoTrack.Clips.Add(new MediaClip
        {
            MediaSourceId = clipA.Id,
            Source = clipA,
            SourceIn = TimeSpan.Zero,
            SourceOut = clipA.Duration,
            Streams = ClipStreams.Both,
            Framerate = framerate,
            TimelineStart = 0,
        });
        videoTrack.Clips.Add(new MediaClip
        {
            MediaSourceId = clipB.Id,
            Source = clipB,
            SourceIn = TimeSpan.Zero,
            SourceOut = clipB.Duration,
            Streams = ClipStreams.Both,
            Framerate = framerate,
            TimelineStart = 90,
        });

        Track overlayTrack = project.Tracks[2];
        overlayTrack.Clips.Add(new GraphicsClip
        {
            DurationFrames = 60,
            TimelineStart = 30,
        });

        Track audioTrack = project.Tracks[3];
        audioTrack.Clips.Add(new MediaClip
        {
            MediaSourceId = soundtrack.Id,
            Source = soundtrack,
            SourceIn = TimeSpan.Zero,
            SourceOut = soundtrack.Duration,
            Streams = ClipStreams.Audio,
            Framerate = framerate,
            TimelineStart = 0,
        });

        return project;
    }
}
