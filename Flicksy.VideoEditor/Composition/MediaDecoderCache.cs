using System;
using System.Collections.Generic;
using Flicksy.Drawing.Media;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Composition;

/// <summary>
/// Lazily-opened, <c>Clip.Id</c>-keyed cache of <see cref="IMediaDecoder"/>s. Keyed by clip
/// (not source) because two clips of the same source at different source-times each need an
/// independent decode cursor (ADR 0004).
/// <para>
/// One instance backs each render seam — <see cref="SkiaCompositor"/> (video, UI thread) and
/// <see cref="AudioMixer"/> (audio, NAudio thread) hold separate caches (ADR 0005). That
/// gives a <c>Streams=Both</c> clip two decoders, one per stream, each on its own thread —
/// FFmpeg seeks the container rather than a single stream, so sharing one <c>MediaFile</c>
/// across the video and audio cursors would thrash. A cache instance is therefore single-
/// threaded by construction; it does no internal locking.
/// </para>
/// </summary>
public sealed class MediaDecoderCache : IDisposable
{
    private readonly Dictionary<Guid, IMediaDecoder> _decoders = new();
    private bool _disposed;

    /// <summary>
    /// Return the decoder for <paramref name="clip"/>, opening it on first reference. Returns
    /// null when the clip has no resolvable source path or the open/probe fails — the caller
    /// renders nothing for the clip (which already reds-out in the timeline). Decoders are
    /// never evicted during the cache's lifetime; all are disposed in <see cref="Dispose"/>.
    /// </summary>
    public IMediaDecoder? GetOrCreate(MediaClip clip, int targetSampleRate)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MediaDecoderCache));
        if (_decoders.TryGetValue(clip.Id, out var existing)) return existing;

        var path = clip.Source?.SourcePath;
        if (string.IsNullOrEmpty(path)) return null;

        try
        {
            var decoder = new FFmpegMediaDecoder(path, targetSampleRate);
            _decoders[clip.Id] = decoder;
            return decoder;
        }
        catch
        {
            // Probe failure — render nothing for this clip. Silent for now, matching the
            // rest of the pipeline; a diagnostics hook lands with the logging work.
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var decoder in _decoders.Values)
        {
            try { decoder.Dispose(); } catch { /* swallow — best-effort cleanup */ }
        }
        _decoders.Clear();
    }
}
