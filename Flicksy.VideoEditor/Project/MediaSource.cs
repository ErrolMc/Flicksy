using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;

namespace Flicksy.VideoEditor.Project;

/// <summary>
/// First-class record of one imported media file the project knows about. Lives in
/// <see cref="Project.MediaSources"/>; the media bin UI is a view over that collection.
/// <see cref="MediaClip"/>s reference a source by <see cref="Id"/> so relocating one
/// missing file fixes every clip that used it (see ADR 0003).
/// <para>
/// Probed at import via <see cref="Probe(string)"/>; the static factory throws on
/// <see cref="MediaFile.Open(string, MediaOptions)"/> failure and the caller is
/// responsible for surfacing the per-file error. <see cref="IsMissing"/> is the runtime
/// flag for a source whose file is no longer openable — distinct from import-time probe
/// failure, where no entry is added in the first place.
/// </para>
/// </summary>
public partial class MediaSource : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();

    [ObservableProperty]
    private string sourcePath = string.Empty;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private TimeSpan duration;

    [ObservableProperty]
    private bool hasVideo;

    [ObservableProperty]
    private bool hasAudio;

    // Video-only metadata. Zero/null on audio-only sources.
    [ObservableProperty]
    private int width;

    [ObservableProperty]
    private int height;

    [ObservableProperty]
    private double sourceFramerate;

    [ObservableProperty]
    private string videoCodec = string.Empty;

    // FFmpeg's lowercase pixel-format name, e.g. "yuv420p".
    [ObservableProperty]
    private string pixelFormat = string.Empty;

    [ObservableProperty]
    private bool isVariableFramerate;

    // Audio-only metadata. Zero on video-only sources.
    [ObservableProperty]
    private int sampleRate;

    [ObservableProperty]
    private int channelCount;

    [ObservableProperty]
    private string audioCodec = string.Empty;

    // Container-level metadata. Zero when unknown (e.g. stub sources). Bitrate is
    // FFmpeg's container bit_rate in bits/s (FFMediaToolkit's XML doc says B/s, but the
    // value is a raw copy of AVFormatContext.bit_rate).
    [ObservableProperty]
    private long fileSizeBytes;

    [ObservableProperty]
    private long bitrate;

    // Runtime flag — true if the source's file is no longer openable. Flipped by
    // MediaBinViewModel's on-focus File.Exists pass (both directions — present→missing
    // and missing→present, where the latter triggers a re-probe) and by the explicit
    // Relocate re-probe. Load-time detection (on project open) lands with save/load.
    [ObservableProperty]
    private bool isMissing;

    public static MediaSource Probe(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var options = new MediaOptions
        {
            StreamsToLoad = MediaMode.AudioVideo,
            VideoPixelFormat = ImagePixelFormat.Bgra32,
        };

        using MediaFile file = MediaFile.Open(fullPath, options);

        var source = new MediaSource
        {
            SourcePath = fullPath,
            DisplayName = Path.GetFileNameWithoutExtension(fullPath),
            HasVideo = file.HasVideo,
            HasAudio = file.HasAudio,
            FileSizeBytes = file.Info.FileInfo?.Length ?? 0,
            Bitrate = file.Info.Bitrate,
        };

        var duration = TimeSpan.Zero;

        if (file.HasVideo)
        {
            VideoStreamInfo info = file.Video.Info;
            source.Width = info.FrameSize.Width;
            source.Height = info.FrameSize.Height;
            source.SourceFramerate = info.AvgFrameRate;
            source.VideoCodec = info.CodecName;
            source.PixelFormat = info.PixelFormat;
            source.IsVariableFramerate = info.IsVariableFrameRate;
            if (info.Duration > duration)
                duration = info.Duration;
        }

        if (file.HasAudio)
        {
            AudioStreamInfo info = file.Audio.Info;
            source.SampleRate = info.SampleRate;
            source.ChannelCount = info.NumChannels;
            source.AudioCodec = info.CodecName;
            if (info.Duration > duration)
                duration = info.Duration;
        }

        source.Duration = duration;
        return source;
    }

    /// <summary>
    /// Copies every probe-derived metadata field from <paramref name="probe"/> onto this
    /// instance and clears <see cref="IsMissing"/>. Identity (<see cref="Id"/>) and naming
    /// (<see cref="SourcePath"/>, <see cref="DisplayName"/>) are untouched — relocate
    /// handles those itself. Single home for the field list so Relocate and the
    /// missing→present re-probe can't drift apart as fields are added.
    /// </summary>
    public void ApplyProbe(MediaSource probe)
    {
        Duration = probe.Duration;
        HasVideo = probe.HasVideo;
        HasAudio = probe.HasAudio;
        Width = probe.Width;
        Height = probe.Height;
        SourceFramerate = probe.SourceFramerate;
        VideoCodec = probe.VideoCodec;
        PixelFormat = probe.PixelFormat;
        IsVariableFramerate = probe.IsVariableFramerate;
        SampleRate = probe.SampleRate;
        ChannelCount = probe.ChannelCount;
        AudioCodec = probe.AudioCodec;
        FileSizeBytes = probe.FileSizeBytes;
        Bitrate = probe.Bitrate;
        IsMissing = false;
    }
}
