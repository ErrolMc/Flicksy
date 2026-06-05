using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Undo.Commands;

/// <summary>
/// Flips a <see cref="MediaClip.Streams"/> value between two states — the source-clip half of
/// <see cref="ViewModels.TimelineViewModel.DetachAudio"/>, which drops the source from
/// <see cref="ClipStreams.Both"/> to <see cref="ClipStreams.Video"/> when its audio is split onto a
/// new track. Kept separate from the audio clip's <see cref="AddClipCommand"/> so undo restores the
/// source's audio independently of removing the detached clip.
/// <para>
/// The clip already holds the <c>after</c> value when this is pushed (the edit mutated live), matching
/// the "push after mutation" convention. Touches no selection — the bundling <c>CompositeCommand</c>'s
/// <c>TimelineSelectionScope</c> owns that. Assigning <see cref="MediaClip.Streams"/> refreshes the
/// clip's <c>DisplayName</c> / <c>IsBroken</c> in both directions.
/// </para>
/// </summary>
public sealed class ChangeClipStreamsCommand : IUndoableCommand
{
    private readonly MediaClip _clip;
    private readonly ClipStreams _before;
    private readonly ClipStreams _after;

    public ChangeClipStreamsCommand(MediaClip clip, ClipStreams before, ClipStreams after)
    {
        _clip = clip;
        _before = before;
        _after = after;
    }

    public void Redo() => _clip.Streams = _after;

    public void Undo() => _clip.Streams = _before;
}
