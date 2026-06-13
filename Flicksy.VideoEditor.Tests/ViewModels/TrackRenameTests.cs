using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.Tests.ViewModels;

/// <summary>
/// Headless coverage of <see cref="Track"/> inline-rename (<c>BeginRename</c> / <c>CommitRename</c> /
/// <c>CancelRename</c>), mirroring the <see cref="MediaClip"/> rename flow but without the auto-derive
/// fallback: a track always keeps a non-empty name, so empty / whitespace input is treated as a cancel.
/// </summary>
[TestFixture]
public class TrackRenameTests
{
    [Test]
    public void BeginRename_SeedsBufferWithCurrentName_AndEntersEditing()
    {
        var track = new Track { Kind = TrackKind.Video, Name = "Video 1" };

        track.BeginRename();

        Assert.That(track.IsEditing, Is.True);
        Assert.That(track.EditingName, Is.EqualTo("Video 1"));
    }

    [Test]
    public void CommitRename_WritesTrimmedBuffer_AndExitsEditing()
    {
        var track = new Track { Kind = TrackKind.Video, Name = "Video 1" };
        track.BeginRename();
        track.EditingName = "  B-roll  ";

        track.CommitRename();

        Assert.That(track.Name, Is.EqualTo("B-roll"));
        Assert.That(track.IsEditing, Is.False);
        Assert.That(track.EditingName, Is.Empty);
    }

    [Test]
    public void CommitRename_EmptyOrWhitespace_KeepsOldName()
    {
        var track = new Track { Kind = TrackKind.Audio, Name = "Audio" };
        track.BeginRename();
        track.EditingName = "   ";

        track.CommitRename();

        Assert.That(track.Name, Is.EqualTo("Audio"), "empty input is treated as cancel — a track keeps a name");
        Assert.That(track.IsEditing, Is.False);
    }

    [Test]
    public void CommitRename_WhenNotEditing_IsNoOp()
    {
        var track = new Track { Kind = TrackKind.Video, Name = "Video 1" };
        track.EditingName = "ignored";   // not in an edit session (mimics the LostFocus-after-Enter re-fire)

        track.CommitRename();

        Assert.That(track.Name, Is.EqualTo("Video 1"));   // the !IsEditing double-fire guard held
    }

    [Test]
    public void CancelRename_DiscardsBuffer_AndKeepsName()
    {
        var track = new Track { Kind = TrackKind.Video, Name = "Video 1" };
        track.BeginRename();
        track.EditingName = "Discarded";

        track.CancelRename();

        Assert.That(track.Name, Is.EqualTo("Video 1"));
        Assert.That(track.IsEditing, Is.False);
        Assert.That(track.EditingName, Is.Empty);
    }
}
