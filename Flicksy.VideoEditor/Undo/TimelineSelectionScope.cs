using System.Collections.Generic;
using System.Linq;
using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.ViewModels;

namespace Flicksy.VideoEditor.Undo;

/// <summary>
/// The video editor's <see cref="ICompositeSelectionScope"/>: lets a <c>CompositeCommand</c>
/// (multi-clip move / delete) capture the whole timeline selection before running its children
/// and restore it afterward, so the inner per-clip commands — each of which re-selects its own
/// clip — don't leave only the last child's clip selected. Mirrors PostSnip's
/// <c>DrawingSelectionScope</c>; restores only clips still present in the project, and rebuilds
/// the set + primary through <see cref="TimelineViewModel.SetSelection"/> so the selection
/// invariant holds.
/// </summary>
public sealed class TimelineSelectionScope : ICompositeSelectionScope
{
    private readonly TimelineViewModel _viewModel;

    public TimelineSelectionScope(TimelineViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public object? Capture() => new Snapshot(_viewModel.SelectedClips.ToList(), _viewModel.SelectedClip);

    public void Restore(object? token)
    {
        if (token is not Snapshot snapshot) 
            return;

        // Drop any clip a child command removed from the document (e.g. a delete bundle) so we
        // never re-select a clip that no longer lives on a track.
        List<Clip> present = snapshot.Clips.Where(c => _viewModel.FindTrack(c) is not null).ToList();
        Clip? primary = snapshot.Primary is not null && present.Contains(snapshot.Primary) ? snapshot.Primary : null;
        _viewModel.SetSelection(present, primary);
    }

    private sealed record Snapshot(IReadOnlyList<Clip> Clips, Clip? Primary);
}
