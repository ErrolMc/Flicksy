using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Flicksy.VideoEditor.Project;

/// <summary>
/// One horizontal lane on the timeline. Owns an ordered <see cref="Clips"/> collection and
/// a sibling <see cref="Transitions"/> list keyed by adjacent-clip pairs. The
/// <see cref="Kind"/> is fixed at construction and determines which clip subtypes are
/// valid here and how the compositor layers the track's output.
/// </summary>
public partial class Track : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TrackKind Kind { get; init; }

    [ObservableProperty]
    private string name = string.Empty;

    /// <summary>Excluded from the audio mix when true. UI hides the M toggle on Overlay headers (those tracks never carry audio); Video tracks keep it because their clips may have <c>Streams=Both</c>.</summary>
    [ObservableProperty]
    private bool muted;

    /// <summary>Editor refuses edits to clips on this track. Compositor unaffected.</summary>
    [ObservableProperty]
    private bool locked;

    /// <summary>Compositor skips the track entirely; the row ghosts in the timeline UI.</summary>
    [ObservableProperty]
    private bool disabled;

    // Inline-rename buffer for the track-header name. Flipped true by BeginRename; TrackHeaderView
    // swaps the name TextBlock for a TextBox bound to EditingName. Transient — not serialized; the
    // persisted state is just Name.
    [JsonIgnore]
    [ObservableProperty]
    private bool isEditing;

    [JsonIgnore]
    [ObservableProperty]
    private string editingName = string.Empty;

    public ObservableCollection<Clip> Clips { get; } = new();

    public List<Transition> Transitions { get; } = new();

    /// <summary>
    /// Removes and returns every <see cref="Transition"/> that references <paramref name="clip"/>
    /// (as either participant). Called when the clip is deleted or moved off the track: its
    /// adjacency is broken, so any transition on its edges is no longer valid (ADR 0006). The
    /// returned list lets the undo command restore them. <see cref="Transitions"/> stays empty
    /// until #14 creates transitions — this is forward-looking integrity.
    /// </summary>
    public IReadOnlyList<Transition> RemoveTransitionsFor(Clip clip)
    {
        List<Transition> removed = Transitions.Where(t => t.LeftClipId == clip.Id || t.RightClipId == clip.Id).ToList();
        foreach (Transition t in removed) 
            Transitions.Remove(t);

        return removed;
    }

    /// <summary>
    /// Reassigns transitions on the outer edges of <paramref name="original"/> when it splits into
    /// <paramref name="leftHalf"/> (keeps the in/left edge) and <paramref name="rightHalf"/> (keeps
    /// the out/right edge): a transition where the original is the left participant — i.e. on its
    /// right edge — moves to the right half, and one where it's the right participant (its left edge)
    /// moves to the left half (ADR 0006). When the original is itself kept as the left half, the
    /// left-edge case is a no-op. <see cref="Transition.LeftClipId"/>/<see cref="Transition.RightClipId"/>
    /// are init-only, so a reassigned transition is replaced with an equivalent carrying the new pair.
    /// Empty until #14 — forward-looking integrity.
    /// </summary>
    public void ReassignTransitionsForSplit(Clip original, Clip leftHalf, Clip rightHalf)
    {
        for (int i = 0; i < Transitions.Count; i++)
        {
            Transition t = Transitions[i];
            if (t.LeftClipId == original.Id && rightHalf.Id != original.Id)
            {
                Transitions[i] = new Transition { LeftClipId = rightHalf.Id, RightClipId = t.RightClipId, Type = t.Type, Duration = t.Duration };
            }
            else if (t.RightClipId == original.Id && leftHalf.Id != original.Id)
            {
                Transitions[i] = new Transition { LeftClipId = t.LeftClipId, RightClipId = leftHalf.Id, Type = t.Type, Duration = t.Duration };
            }
        }
    }

    /// <summary>
    /// Replaces the <see cref="Transitions"/> contents with <paramref name="snapshot"/>. Used by the
    /// split / delete undo commands to restore a captured before/after transition list in one step.
    /// </summary>
    public void ReplaceTransitions(IReadOnlyList<Transition> snapshot)
    {
        Transitions.Clear();
        Transitions.AddRange(snapshot);
    }

    /// <summary>
    /// Opens the inline-rename TextBox on this track's header, seeding the buffer with the current
    /// <see cref="Name"/>. Idempotent — calling it while already editing just re-seeds.
    /// </summary>
    public void BeginRename()
    {
        EditingName = Name;
        IsEditing = true;
    }

    /// <summary>
    /// Closes the rename editor and writes the trimmed buffer to <see cref="Name"/>. Empty /
    /// whitespace input is treated as a cancel — a track always keeps a non-empty name (there's no
    /// auto-derived fallback as on <see cref="MediaClip"/>). The <c>!IsEditing</c> early return guards
    /// the LostFocus-after-Enter double-fire (Enter commits and collapses the TextBox, whose collapse
    /// raises LostFocus and re-enters here).
    /// </summary>
    public void CommitRename()
    {
        if (!IsEditing)
            return;

        string trimmed = (EditingName ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(trimmed))
            Name = trimmed;

        IsEditing = false;
        EditingName = string.Empty;
    }

    /// <summary>
    /// Closes the rename editor without writing the buffer.
    /// </summary>
    public void CancelRename()
    {
        IsEditing = false;
        EditingName = string.Empty;
    }
}
