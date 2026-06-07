using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;
using Flicksy.Drawing.Helpers;
using Flicksy.VideoEditor.Project;
using Microsoft.Win32;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Backs the media bin (#9 steps 1a + 1b). Owns a wrapper collection
/// (<see cref="MediaSources"/>) that mirrors <see cref="Project.Project.MediaSources"/>
/// 1:1, plus single-selection state and the Import / Reveal / BeginRename / CommitRename /
/// CancelRename / Relocate / Remove commands. Probe runs on the UI thread (50–150 ms per
/// file, sequential for multi-file imports); thumbnail decode is off-loaded to a single
/// background worker over an unbounded <see cref="Channel{T}"/> and posted back via the UI
/// dispatcher. Audio-only sources short-circuit the worker and get the static music-file
/// glyph immediately. Relocate preserves the source's <see cref="MediaSource.Id"/> so
/// every referencing <see cref="MediaClip"/> stays linked; Remove cascades through every
/// referencing clip plus any <see cref="Transition"/> touching those clips.
/// </summary>
public sealed partial class MediaBinViewModel : ObservableObject
{
    // Extensions accepted by the Import dialog filter and the Explorer drag-drop path.
    // Must stay in sync between the two so dedup + probe-failure handling are uniform.
    private static readonly HashSet<string> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".webm", ".avi", ".m4v",
        ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg",
    };

    private const string DialogFilter =
        "Media files|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.m4v;*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg|All files|*.*";

    private readonly Project.Project _project;
    private readonly Dispatcher _dispatcher;
    private readonly Channel<MediaSource> _thumbnailQueue;
    private readonly Dictionary<Guid, MediaSourceViewModel> _wrappersById = new();
    private ImageSource? _audioGlyph;

    public MediaBinViewModel(Project.Project project)
    {
        _project = project;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        MediaSources = new ObservableCollection<MediaSourceViewModel>();

        foreach (var source in project.MediaSources)
        {
            AddWrapper(source);
        }
        project.MediaSources.CollectionChanged += OnProjectMediaSourcesChanged;

        _thumbnailQueue = Channel.CreateUnbounded<MediaSource>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });
        // Fire-and-forget worker. Lives for the lifetime of the editor window.
        _ = Task.Run(ProcessThumbnailsAsync);
    }

    public ObservableCollection<MediaSourceViewModel> MediaSources { get; }

    [ObservableProperty]
    private MediaSourceViewModel? selectedSource;

    public bool IsEmpty => MediaSources.Count == 0;

    public bool HasEntries => MediaSources.Count > 0;

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = DialogFilter,
            Title = "Import media",
        };
        if (dialog.ShowDialog() != true) 
            return;

        foreach (var path in dialog.FileNames)
        {
            TryImportFile(path);
        }
    }

    public void TryImportFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) 
                continue;

            string? ext = Path.GetExtension(path);
            if (!AcceptedExtensions.Contains(ext)) 
                continue;

            TryImportFile(path);
        }
    }

    private void TryImportFile(string rawPath)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (Exception ex)
        {
            ShowImportError(rawPath, ex);
            return;
        }

        // Silent dedupe — already-imported sources skip without a message.
        if (_project.MediaSources.Any(s => string.Equals(s.SourcePath, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        MediaSource source;
        try
        {
            source = MediaSource.Probe(fullPath);
        }
        catch (Exception ex)
        {
            ShowImportError(fullPath, ex);
            return;
        }

        _project.MediaSources.Add(source);
    }

    [RelayCommand]
    private void Reveal(MediaSourceViewModel? entry)
    {
        string? path = entry?.Source.SourcePath;
        if (string.IsNullOrWhiteSpace(path)) 
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch
        {
            // Reveal is best-effort — explorer.exe can fail on a deleted file or restricted path.
        }
    }

    [RelayCommand]
    private void BeginRename(MediaSourceViewModel? entry)
    {
        if (entry is null) 
            return;

        if (entry.IsEditing) 
            return;

        // Single-editor — commit any in-progress rename on a different entry first so a
        // half-finished name isn't silently dropped when focus shifts.
        foreach (MediaSourceViewModel other in MediaSources)
        {
            if (other != entry && other.IsEditing)
            {
                CommitRename(other);
            }
        }

        entry.EditingName = entry.Source.DisplayName;
        entry.IsEditing = true;
    }

    [RelayCommand]
    private void CommitRename(MediaSourceViewModel? entry)
    {
        if (entry is null) 
            return;

        // Guards against the double-fire path: Enter sets IsEditing=false, which collapses
        // the TextBox, which fires LostFocus, which calls CommitRename a second time.
        if (!entry.IsEditing) 
            return;

        string newName = (entry.EditingName ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(newName))
        {
            entry.Source.DisplayName = newName;
        }
        // Empty / whitespace-only input is treated as a cancel — DisplayName must always
        // hold something so the bin cell and clip labels never go blank.
        entry.IsEditing = false;
    }

    [RelayCommand]
    private void CancelRename(MediaSourceViewModel? entry)
    {
        if (entry is null) 
            return;

        entry.IsEditing = false;
    }

    [RelayCommand]
    private void Relocate(MediaSourceViewModel? entry)
    {
        if (entry is null) 
            return;

        MediaSource source = entry.Source;

        var dialog = new OpenFileDialog
        {
            Filter = DialogFilter,
            Title = $"Relocate \"{source.DisplayName}\"",
            FileName = Path.GetFileName(source.SourcePath),
        };

        if (dialog.ShowDialog() != true) 
            return;

        string newPath;
        try
        {
            newPath = Path.GetFullPath(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowRelocateError(dialog.FileName, ex);
            return;
        }

        // Dedup against other bin entries — relocating into a path that's already imported
        // would leave two sources pointing at the same file and confuse subsequent edits.
        if (_project.MediaSources.Any(s => s != source && string.Equals(s.SourcePath, newPath, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                $"\"{Path.GetFileName(newPath)}\" is already imported as another source. Remove that entry first if you want to use this file.",
                "Already imported",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MediaSource probe;
        try
        {
            probe = MediaSource.Probe(newPath);
        }
        catch (Exception ex)
        {
            ShowRelocateError(newPath, ex);
            return;
        }

        if (probe.Duration != source.Duration)
        {
            MessageBoxResult result = MessageBox.Show(
                $"Duration changed from {FormatDuration(source.Duration)} to {FormatDuration(probe.Duration)}. " +
                "Clips that reference this source may now extend past the new end and need trimming.\n\nApply anyway?",
                "Duration changed",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.OK) 
                return;
        }

        // Apply onto the existing MediaSource instance — Id is preserved, so every clip
        // that referenced this source via MediaSourceId stays linked. Mutations propagate
        // via [ObservableProperty] change notifications.
        string? oldAutoName = Path.GetFileNameWithoutExtension(source.SourcePath);
        source.SourcePath = newPath;
        // Smart auto-name: if the user never renamed (DisplayName still matches what import
        // would have generated from the old path), update it to match the new file. If they
        // did rename, keep their explicit name — rename + relocate compose cleanly.
        if (string.Equals(source.DisplayName, oldAutoName, StringComparison.Ordinal))
        {
            source.DisplayName = Path.GetFileNameWithoutExtension(newPath);
        }
        source.Duration = probe.Duration;
        source.HasVideo = probe.HasVideo;
        source.HasAudio = probe.HasAudio;
        source.Width = probe.Width;
        source.Height = probe.Height;
        source.SourceFramerate = probe.SourceFramerate;
        source.SampleRate = probe.SampleRate;
        source.ChannelCount = probe.ChannelCount;
        source.IsMissing = false;

        // The existing thumbnail (if any) is now stale — IssueThumbnail clears it and
        // re-issues per the new stream layout. Minor race: if the worker is mid-decode
        // of the OLD path when we enqueue the new one, the old thumb may land first
        // and be overwritten by the new one a moment later. Acceptable flicker; a
        // per-source generation guard is overkill today.
        IssueThumbnail(entry);
    }

    [RelayCommand]
    private void Remove(MediaSourceViewModel? entry)
    {
        if (entry is null) return;
        var source = entry.Source;

        // Find every MediaClip referencing this source, paired with its parent Track, so
        // the cascade can splice both the clip and any transition touching it. Track has no
        // back-reference from Clip, so we walk Project.Tracks rather than the clip itself.
        var referencing = new List<(Track Track, MediaClip Clip)>();
        foreach (Track track in _project.Tracks)
        {
            foreach (Clip clip in track.Clips)
            {
                if (clip is MediaClip mc && mc.MediaSourceId == source.Id)
                {
                    referencing.Add((track, mc));
                }
            }
        }

        if (referencing.Count > 0)
        {
            MessageBoxResult result = MessageBox.Show(
                $"\"{source.DisplayName}\" is used by {referencing.Count} clip(s) on the timeline.\n\n" +
                "Remove the source and delete those clips?",
                "Remove source",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) 
                return;
        }

        // Cascade: remove each referencing clip plus any transitions on its track that
        // touched it. A transition that referenced two cascaded clips disappears on the
        // first pass — the second pass simply finds nothing.
        foreach ((Track track, MediaClip clip) in referencing)
        {
            track.Clips.Remove(clip);
            Guid clipId = clip.Id;
            track.Transitions.RemoveAll(t => t.LeftClipId == clipId || t.RightClipId == clipId);
        }

        _project.MediaSources.Remove(source);
    }

    /// <summary>
    /// Mid-session missing-source detection — called by the editor window from
    /// <c>OnActivated</c>. Snapshots <see cref="Project.Project.MediaSources"/> on the UI
    /// thread (safe iteration even if an Import/Remove fires during the scan), then walks
    /// each source on a background task with a cheap <see cref="File.Exists(string)"/>
    /// check. Per-source flips are dispatcher-posted: present→missing clears the cached
    /// thumbnail; missing→present runs a full <see cref="MediaSource.Probe"/> and
    /// re-issues the thumbnail via <see cref="IssueThumbnail"/>. Silent on probe-throw
    /// (the file reappeared but isn't openable — leave IsMissing set). No debouncing —
    /// File.Exists is cheap enough that even rapid alt-tabs are fine; dispatcher posts
    /// serialize naturally on the UI thread so concurrent scans converge to a consistent
    /// state. Load-time detection (on project open) will use the same primitive.
    /// </summary>
    public void RefreshMissingState()
    {
        MediaSource[] snapshot = _project.MediaSources.ToArray();
        if (snapshot.Length == 0) 
            return;

        _ = Task.Run(() => ScanMissingState(snapshot));
    }

    private void ScanMissingState(IReadOnlyList<MediaSource> sources)
    {
        foreach (MediaSource source in sources)
        {
            string path = source.SourcePath;
            if (string.IsNullOrWhiteSpace(path)) 
                continue;

            bool exists;
            try 
            { 
                exists = File.Exists(path); 
            }
            catch 
            { 
                exists = false;  // malformed path / IO error — treat as missing
            } 

            // No-op if state already matches reality (the common case on every alt-tab).
            if (exists == !source.IsMissing) 
                continue;

            if (!exists)
            {
                // File disappeared. Flip on the UI thread and drop the cached thumbnail
                // so the cell renders as pure "?" rather than a stale frame under the
                // placeholder overlay.
                _dispatcher.InvokeAsync(() =>
                {
                    source.IsMissing = true;
                    if (_wrappersById.TryGetValue(source.Id, out var wrapper))
                    {
                        wrapper.Thumbnail = null;
                    }
                });
            }
            else
            {
                // Was missing, file is back — re-probe in case it was replaced with a
                // different file (different duration, different stream layout). Silent
                // on probe-throw: the file exists at the path but can't be opened, so
                // leave the missing flag set rather than corrupt the metadata.
                MediaSource probe;
                try 
                { 
                    probe = MediaSource.Probe(path); 
                }
                catch 
                { 
                    continue; 
                }

                _dispatcher.InvokeAsync(() => ApplyReprobe(source, probe));
            }
        }
    }

    private void ApplyReprobe(MediaSource source, MediaSource probe)
    {
        source.Duration = probe.Duration;
        source.HasVideo = probe.HasVideo;
        source.HasAudio = probe.HasAudio;
        source.Width = probe.Width;
        source.Height = probe.Height;
        source.SourceFramerate = probe.SourceFramerate;
        source.SampleRate = probe.SampleRate;
        source.ChannelCount = probe.ChannelCount;
        source.IsMissing = false;

        if (_wrappersById.TryGetValue(source.Id, out var wrapper))
        {
            IssueThumbnail(wrapper);
        }
    }

    private void OnProjectMediaSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (MediaSource source in e.OldItems)
            {
                RemoveWrapper(source);
            }
        }
        if (e.NewItems is not null)
        {
            foreach (MediaSource source in e.NewItems)
            {
                AddWrapper(source);
            }
        }
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _wrappersById.Clear();
            MediaSources.Clear();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasEntries));
        }
    }

    private void AddWrapper(MediaSource source)
    {
        var wrapper = new MediaSourceViewModel(source);
        IssueThumbnail(wrapper);
        _wrappersById[source.Id] = wrapper;
        MediaSources.Add(wrapper);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasEntries));
    }

    private void RemoveWrapper(MediaSource source)
    {
        if (!_wrappersById.Remove(source.Id, out var wrapper)) 
            return;

        MediaSources.Remove(wrapper);
        if (SelectedSource == wrapper) 
            SelectedSource = null;

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasEntries));
    }

    // Re-issue the cell's thumbnail based on the source's current stream layout and
    // IsMissing state. Audio-only gets the static glyph synchronously; video gets
    // enqueued for background decode; missing sources get nothing (the cell paints
    // a red "?" placeholder via the DataTemplate). Called on initial AddWrapper, on
    // Relocate (after metadata refresh), and on the on-focus re-probe pass when a
    // previously-missing source comes back.
    private void IssueThumbnail(MediaSourceViewModel wrapper)
    {
        MediaSource source = wrapper.Source;
        wrapper.Thumbnail = null;
        if (source.IsMissing) return;
        if (source.HasAudio && !source.HasVideo)
        {
            wrapper.Thumbnail = GetAudioGlyph();
        }
        else if (source.HasVideo)
        {
            _thumbnailQueue.Writer.TryWrite(source);
        }
    }

    private ImageSource GetAudioGlyph()
    {
        // Lazy first-touch — never accessed on stub-only projects that have no audio.
        return _audioGlyph ??= Images.music_file.ToImageSource();
    }

    private async Task ProcessThumbnailsAsync()
    {
        await foreach (MediaSource source in _thumbnailQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            ImageSource? thumb;
            try
            {
                thumb = DecodeVideoThumbnail(source.SourcePath);
            }
            catch
            {
                // Decode failure keeps the placeholder. Silent per spec — runtime IsMissing
                // flips only on explicit re-probe, never on thumbnail failure alone.
                continue;
            }

            if (thumb is null) 
                continue;

            _ = _dispatcher.InvokeAsync(() =>
            {
                if (_wrappersById.TryGetValue(source.Id, out var wrapper))
                {
                    wrapper.Thumbnail = thumb;
                }
            });
        }
    }

    private static ImageSource? DecodeVideoThumbnail(string path)
    {
        var options = new MediaOptions
        {
            StreamsToLoad = MediaMode.Video,
            VideoPixelFormat = ImagePixelFormat.Bgra32,
        };

        using MediaFile file = MediaFile.Open(path, options);
        if (!file.HasVideo) 
            return null;

        TimeSpan duration = file.Video.Info.Duration;
        TimeSpan seekTo = duration > TimeSpan.Zero
            ? TimeSpan.FromTicks(duration.Ticks / 2)
            : TimeSpan.Zero;

        if (!file.Video.TryGetFrame(seekTo, out var frame)) 
            return null;

        int width = frame.ImageSize.Width;
        int height = frame.ImageSize.Height;
        if (width <= 0 || height <= 0) 
            return null;

        // BitmapSource.Create copies the pixel buffer internally — safe to dispose
        // MediaFile after this call without dangling pointers.
        var source = BitmapSource.Create(
            width,
            height,
            96, 96,
            PixelFormats.Bgra32,
            palette: null,
            pixels: frame.Data.ToArray(),
            stride: frame.Stride);

        const double TargetWidth = 200.0;
        double scale = TargetWidth / width;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    private void ShowImportError(string path, Exception ex)
    {
        var fileName = string.Empty;
        try 
        { 
            fileName = Path.GetFileName(path); 
        } 
        catch 
        { 
            fileName = path; 
        }

        MessageBox.Show(
            $"Couldn't import {fileName}:\n\n{ex.Message}",
            "Import failed",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ShowRelocateError(string path, Exception ex)
    {
        var fileName = string.Empty;
        try 
        { 
            fileName = Path.GetFileName(path); 
        } 
        catch 
        { 
            fileName = path; 
        }

        MessageBox.Show(
            $"Couldn't open {fileName}:\n\n{ex.Message}",
            "Relocate failed",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string FormatDuration(TimeSpan d) =>
        d < TimeSpan.FromHours(1)
            ? $"{(int)d.TotalMinutes}:{d.Seconds:D2}"
            : d.ToString(@"h\:mm\:ss");

    public static bool IsAcceptedMediaPath(string path)
        => !string.IsNullOrWhiteSpace(path) && AcceptedExtensions.Contains(Path.GetExtension(path));
}
