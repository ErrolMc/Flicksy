using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.Undo;
using Flicksy.Drawing.ViewModels;
using Flicksy.VideoEditor.Project;
using Flicksy.VideoEditor.Undo;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>The graphics overlay's armed tool. No Pen/Erase — those are snip-only (ADR 0013).</summary>
public enum GraphicsTool
{
    Select,
    Shape,
    Text,
}

/// <summary>
/// Coordinates the graphics-editing overlay over the Preview (issue #13 / ADR 0013). One graphic
/// object per <see cref="GraphicsClip"/>, the Clipchamp / Final Cut model. Two <see cref="DrawingViewModel"/>s
/// back one on-screen <c>DrawingView</c> (its <c>DataContext</c> is <see cref="ActiveOverlay"/>):
/// <list type="bullet">
///   <item><b>Placement</b> (Shapes/Text armed): a throwaway VM with its own collection + undo stack.
///   When the user finishes drawing, the item is lifted into a new <see cref="GraphicsClip"/> at the
///   playhead and the clip-add (+ an auto-created Overlay track) is recorded on the shared stack as one
///   undo step — the transient draw never pollutes the editor history. Placing auto-switches to Select.</item>
///   <item><b>Edit</b> (Select + a graphics clip selected): a VM wrapping that clip's single item + the
///   editor's shared <see cref="UndoManager"/>, so move/scale/rotate (and left-panel restyle) record
///   clip-level undo. The edited clip is suppressed from the preview composite so the overlay is its sole
///   renderer (no double-draw).</item>
/// </list>
/// Right-rail transform inspector stays #15; style is edited through the shared Shapes/Text panels.
/// </summary>
public partial class GraphicsEditorViewModel : ObservableObject
{
    private const int DefaultDurationSeconds = 3;

    private readonly Project.Project _project;
    private readonly TimelineViewModel _timeline;
    private readonly TransportViewModel _transport;
    private readonly PreviewViewModel _preview;
    private readonly UndoManager _history;
    private readonly Dispatcher _dispatcher;

    // Placement surface: NEW objects draw into this throwaway VM (own collection + own undo stack).
    private readonly UndoManager _placementHistory = new();
    private readonly DrawingViewModel _placement;

    private DrawingViewModel? _editVm;
    private GraphicsClip? _editingClip;
    private LeftRailTab _currentTab = LeftRailTab.Media;
    private bool _committing;
    private bool _syncingPanel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectActive))]
    [NotifyPropertyChangedFor(nameof(IsShapeActive))]
    [NotifyPropertyChangedFor(nameof(IsTextActive))]
    private GraphicsTool activeTool = GraphicsTool.Select;

    // The DrawingViewModel the on-screen overlay currently binds to (placement, an edit VM, or none).
    [ObservableProperty]
    private DrawingViewModel? activeOverlay;

    // Drives the overlay's IsHitTestVisible: it intercepts pointer input only when a graphics tool is
    // armed (placement) or a graphics clip is selected for editing — otherwise clicks pass through.
    [ObservableProperty]
    private bool isOverlayActive;

    public GraphicsEditorViewModel(
        Project.Project project,
        TimelineViewModel timeline,
        TransportViewModel transport,
        PreviewViewModel preview,
        UndoManager history)
    {
        _project = project;
        _timeline = timeline;
        _transport = transport;
        _preview = preview;
        _history = history;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _placement = new DrawingViewModel(new ObservableCollection<DrawingItem>(), _placementHistory);
        _placementHistory.PropertyChanged += OnPlacementHistoryChanged;
        ShapeSettings.PropertyChanged += OnShapeSettingsChanged;

        UpdateOverlay();
    }

    /// <summary>Shape kind + fill/outline for the next placed shape (and the selected shape's restyle).</summary>
    public ShapeSettingsViewModel ShapeSettings { get; } = new();

    /// <summary>Font/size + fill/outline for the next placed text (and the selected text's restyle).</summary>
    public TextSettingsViewModel TextSettings { get; } = new();

    /// <summary>
    /// Selection handles + rotate puck for the overlay's current item. Kept synced to
    /// <see cref="ActiveOverlay"/>'s <c>SelectedItem</c>; the view projects it through the host's
    /// content-to-viewport transform so handles stay screen-sized.
    /// </summary>
    public SelectionOverlayViewModel SelectionOverlay { get; } = new();

    public bool IsSelectActive => ActiveTool == GraphicsTool.Select;

    public bool IsShapeActive => ActiveTool == GraphicsTool.Shape;

    public bool IsTextActive => ActiveTool == GraphicsTool.Text;

    /// <summary>Arms the Text tool for placement — bound to the Text panel's "add text" affordance.</summary>
    [RelayCommand]
    private void ArmText()
    {
        ActiveTool = GraphicsTool.Text;
    }

    /// <summary>Root VM forwards left-rail tab changes here: Shapes/Text arm placement, anything else Select.</summary>
    public void SetLeftTab(LeftRailTab tab)
    {
        _currentTab = tab;
        ActiveTool = tab switch
        {
            LeftRailTab.Shapes => GraphicsTool.Shape,
            LeftRailTab.Text => GraphicsTool.Text,
            _ => GraphicsTool.Select,
        };
    }

    /// <summary>Root VM forwards selection changes here; only Select mode rebinds to the new clip.</summary>
    public void OnSelectionChanged()
    {
        if (ActiveTool == GraphicsTool.Select)
            UpdateOverlay();
    }

    partial void OnActiveToolChanged(GraphicsTool value)
    {
        if (value is GraphicsTool.Shape or GraphicsTool.Text)
            ResetPlacement();
        UpdateOverlay();
    }

    private void OnShapeSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Picking a shape in the Shapes panel re-arms placement (so the user can place another after
        // the auto-switch to Select), but only on a user pick while the Shapes tab is the context —
        // not when BindEdit programmatically syncs the panel to a just-selected shape.
        if (!_syncingPanel
            && e.PropertyName == nameof(ShapeSettingsViewModel.SelectedShape)
            && _currentTab == LeftRailTab.Shapes)
        {
            ActiveTool = GraphicsTool.Shape;
        }
    }

    private void UpdateOverlay()
    {
        if (ActiveTool is GraphicsTool.Shape or GraphicsTool.Text)
        {
            // Placement: draw a new object; nothing is suppressed (the selected clip, if any, stays
            // as a backdrop and the new object draws on top of the composite).
            UnbindEdit();
            ActiveOverlay = _placement;
            _preview.SuppressClip(null);
            IsOverlayActive = true;
        }
        else if (_timeline.SelectedClip is GraphicsClip clip)
        {
            BindEdit(clip);
            ActiveOverlay = _editVm;
            _preview.SuppressClip(clip.Id);
            IsOverlayActive = true;
        }
        else
        {
            UnbindEdit();
            ActiveOverlay = null;
            _preview.SuppressClip(null);
            IsOverlayActive = false;
        }

        SyncSelectionOverlay();
    }

    partial void OnActiveOverlayChanged(DrawingViewModel? oldValue, DrawingViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnOverlayPropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnOverlayPropertyChanged;
    }

    private void OnOverlayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DrawingViewModel.SelectedItem))
            SyncSelectionOverlay();
    }

    private void SyncSelectionOverlay()
    {
        DrawingItem? selected = ActiveOverlay?.SelectedItem;
        SelectionOverlay.SelectedItem = selected;
        SelectionOverlay.IsActive = IsOverlayActive && selected is not null;
    }

    private void BindEdit(GraphicsClip clip)
    {
        if (ReferenceEquals(_editingClip, clip) && _editVm is not null)
            return;

        UnbindEdit();

        var items = new ObservableCollection<DrawingItem>();
        if (clip.Item is { } existing)
            items.Add(existing);
        var vm = new DrawingViewModel(items, _history) { SelectedItem = clip.Item };
        _editVm = vm;
        _editingClip = clip;

        // Open a style-edit session + sync the matching panel so left-panel restyles of the selected
        // item record as one undo step (mirrors PostSnip's popup open/close batching). The sync flag
        // stops the shape sync from being mistaken for a user shape-pick (which would re-arm placement).
        if (clip.Item is ShapeItem shape)
        {
            _syncingPanel = true;
            ShapeSettings.SyncFromShapeItem(shape);
            _syncingPanel = false;
            vm.BeginShapeStyleEdit(shape);
        }
        else if (clip.Item is TextItem text)
        {
            TextSettings.SyncFromTextItem(text);
            vm.BeginTextStyleEdit(text);
        }
    }

    private void UnbindEdit()
    {
        if (_editVm is { } vm)
        {
            // Closes any open style-edit session — pushes a Shape/TextStyleCommand iff the style changed.
            vm.EndShapeStyleEdit();
            vm.EndTextStyleEdit();
        }

        _editVm = null;
        _editingClip = null;
    }

    private void OnPlacementHistoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A committed draw (EndShape / EndEditText pushed an AddItemCommand) flips CanUndo true. Defer
        // the lift-into-clip until after the gesture's pointer-up fully unwinds — mutating tracks and
        // swapping the overlay's DataContext mid-OnPointerUp would tear down the tool still running.
        if (e.PropertyName != nameof(UndoManager.CanUndo) || !_placementHistory.CanUndo || _committing)
            return;

        _committing = true;
        _dispatcher.BeginInvoke(CommitPlacement);
    }

    private void CommitPlacement()
    {
        try
        {
            if (_placement.Items.LastOrDefault() is { } item)
                PlaceObject(item);
        }
        finally
        {
            ResetPlacement();
            _committing = false;
        }
    }

    private void PlaceObject(DrawingItem item)
    {
        int start = _transport.Playhead;
        int duration = Math.Max(1, _project.Settings.Framerate * DefaultDurationSeconds);
        int end = start + duration;
        Track? track = FindFreeOverlayTrack(start, end);

        // One undo step for the whole placement: the (optional) auto-created track + the clip-add.
        _history.Begin();
        bool committed = false;
        try
        {
            track ??= _timeline.AddOverlayTrackOnTop();
            var clip = new GraphicsClip { TimelineStart = start, DurationFrames = duration, Item = item };
            _timeline.AddClip(track, clip); // sorted insert + select + AddClipCommand (captured in the batch)
            _history.Commit(new TimelineSelectionScope(_timeline));
            committed = true;
        }
        finally
        {
            if (!committed)
                _history.Cancel();
        }

        // Auto-switch to Select so the placed object is immediately movable (ADR 0013). AddClip already
        // selected the clip, so OnActiveToolChanged → UpdateOverlay binds the edit overlay to it.
        ActiveTool = GraphicsTool.Select;
    }

    private void ResetPlacement()
    {
        _placement.SelectedItem = null;
        _placement.Items.Clear();
        _placementHistory.Reset();
    }

    private Track? FindFreeOverlayTrack(int start, int end)
    {
        // Top-most (lowest-index) unlocked Overlay track with nothing overlapping [start, end).
        foreach (Track track in _project.Tracks)
        {
            if (track.Kind != TrackKind.Overlay || track.Locked)
                continue;

            bool free = true;
            foreach (Clip clip in track.Clips)
            {
                if (clip.TimelineStart < end && start < clip.TimelineStart + clip.Duration)
                {
                    free = false;
                    break;
                }
            }

            if (free)
                return track;
        }

        return null;
    }
}
