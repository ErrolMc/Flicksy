using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Flicksy.Drawing.Undo;
using Flicksy.PostSnip.Undo.Commands;
using Flicksy.Drawing.Undo.Commands;

namespace Flicksy.PostSnip.ViewModels;

public partial class CropOverlayViewModel : ObservableObject
{
    private const double MinCropSize = 8.0;

    private UndoManager? _history;
    private Rect _editStartCrop = Rect.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(ImageBounds))]
    private double imageWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(ImageBounds))]
    private double imageHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCrop))]
    [NotifyPropertyChangedFor(nameof(CurrentViewBounds))]
    [NotifyPropertyChangedFor(nameof(IsCropped))]
    private Rect committedCrop = Rect.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCrop))]
    private Rect workingCrop = Rect.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCrop))]
    [NotifyPropertyChangedFor(nameof(CurrentViewBounds))]
    private bool isActive;

    public bool HasImage => ImageWidth > 0 && ImageHeight > 0;

    public Rect ImageBounds => new(0, 0, Math.Max(0, ImageWidth), Math.Max(0, ImageHeight));

    // The crop rect the view should draw or clip to right now.
    public Rect EffectiveCrop => IsActive ? WorkingCrop : CommittedCrop;

    // The image bounds that pan/zoom auto-fit should target. While editing,
    // the full image is visible so the user can expand the crop; otherwise the
    // committed crop defines the visible region.
    public Rect CurrentViewBounds => IsActive ? ImageBounds : CommittedCrop;

    public bool IsCropped =>
        HasImage &&
        (CommittedCrop.X > 0 ||
         CommittedCrop.Y > 0 ||
         CommittedCrop.Width < ImageWidth ||
         CommittedCrop.Height < ImageHeight);

    public event EventHandler? ViewBoundsChanged;

    public void AttachHistory(UndoManager history)
    {
        _history = history;
    }

    public void Reset(double imageWidth, double imageHeight)
    {
        IsActive = false;
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        Rect full = ImageBounds;
        CommittedCrop = full;
        WorkingCrop = full;
        _editStartCrop = Rect.Empty;
        ViewBoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void BeginEdit()
    {
        if (!HasImage)
        {
            return;
        }

        _editStartCrop = CommittedCrop;
        WorkingCrop = CommittedCrop;
        IsActive = true;
        ViewBoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CommitEdit()
    {
        if (!IsActive)
        {
            return;
        }

        Rect newCrop = ClampToImage(WorkingCrop);
        if (newCrop.Width < MinCropSize || newCrop.Height < MinCropSize)
        {
            // Reject degenerate selections — keep the previous committed crop.
            newCrop = _editStartCrop.IsEmpty ? ImageBounds : _editStartCrop;
        }

        IsActive = false;

        if (!RectsEqual(newCrop, _editStartCrop))
        {
            Rect before = _editStartCrop;
            CommittedCrop = newCrop;
            WorkingCrop = newCrop;
            _history?.Push(new CropCommand(this, before, newCrop));
        }
        else
        {
            WorkingCrop = CommittedCrop;
        }

        _editStartCrop = Rect.Empty;
        ViewBoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelEdit()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        WorkingCrop = CommittedCrop;
        _editStartCrop = Rect.Empty;
        ViewBoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetWorkingCrop(Rect rect)
    {
        if (!IsActive)
        {
            return;
        }

        WorkingCrop = ClampToImage(rect);
    }

    // Used by CropCommand to apply undo/redo state without re-pushing. If the user is
    // mid-edit the working rect (and the edit baseline) snap to the undone value so
    // that exiting the edit with no further changes won't push a redundant command.
    public void ApplyCommittedCrop(Rect rect)
    {
        Rect clamped = ClampToImage(rect);
        CommittedCrop = clamped;
        WorkingCrop = clamped;
        if (IsActive)
        {
            _editStartCrop = clamped;
        }
        ViewBoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Rect ClampToImage(Rect rect)
    {
        if (!HasImage)
        {
            return Rect.Empty;
        }

        double x = Math.Max(0, Math.Min(rect.X, ImageWidth));
        double y = Math.Max(0, Math.Min(rect.Y, ImageHeight));
        double w = Math.Max(0, Math.Min(rect.Width, ImageWidth - x));
        double h = Math.Max(0, Math.Min(rect.Height, ImageHeight - y));
        return new Rect(x, y, w, h);
    }

    private static bool RectsEqual(Rect a, Rect b)
    {
        const double eps = 0.5;
        return Math.Abs(a.X - b.X) < eps
            && Math.Abs(a.Y - b.Y) < eps
            && Math.Abs(a.Width - b.Width) < eps
            && Math.Abs(a.Height - b.Height) < eps;
    }
}
