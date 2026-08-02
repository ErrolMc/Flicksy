using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Helpers;
using Flicksy.Drawing.Source;

namespace Flicksy.Drawing.ViewModels;

public partial class ShapeOption : ObservableObject
{
    public ShapeOption(ShapeKind kind, string name, ImageSource icon)
    {
        Kind = kind;
        Name = name;
        Icon = icon;
    }

    public ShapeKind Kind { get; }

    public string Name { get; }

    public ImageSource Icon { get; }

    [ObservableProperty]
    private bool isSelected;
}

public partial class ShapeSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFillEnabled))]
    private ShapeOption? selectedShape;

    public bool IsFillEnabled => SelectedShape?.Kind is not (ShapeKind.Line or ShapeKind.Arrow);

    [ObservableProperty]
    private bool isFillSettingsOpen;

    [ObservableProperty]
    private bool isOutlineSettingsOpen;

    private DateTime _lastFillPopupCloseAt = DateTime.MinValue;
    private DateTime _lastOutlinePopupCloseAt = DateTime.MinValue;

    public FillSettingsViewModel FillSettings { get; } = new();

    public OutlineSettingsViewModel OutlineSettings { get; } = new();

    public ObservableCollection<ShapeOption> Shapes { get; }

    public ImageSource CircleOutside { get; } = Images.circle.ToImageSource();

    public ImageSource CircleInside { get; } = Images.circle_inside.ToImageSource();

    public ImageSource DonutOutside { get; } = Images.donut_outside.ToImageSource();

    public ImageSource DonutInside { get; } = Images.donut_inside.ToImageSource();

    public ShapeSettingsViewModel()
    {
        Shapes = new ObservableCollection<ShapeOption>(CreateShapes());
        SelectShape(Shapes[0]);
    }

    [RelayCommand]
    private void ToggleFillSettings()
    {
        if ((DateTime.UtcNow - _lastFillPopupCloseAt).TotalMilliseconds < 250)
        {
            return;
        }

        IsFillSettingsOpen = !IsFillSettingsOpen;
    }

    partial void OnIsFillSettingsOpenChanged(bool value)
    {
        if (!value)
        {
            _lastFillPopupCloseAt = DateTime.UtcNow;
        }
    }

    [RelayCommand]
    private void ToggleOutlineSettings()
    {
        if ((DateTime.UtcNow - _lastOutlinePopupCloseAt).TotalMilliseconds < 250)
        {
            return;
        }

        IsOutlineSettingsOpen = !IsOutlineSettingsOpen;
    }

    partial void OnIsOutlineSettingsOpenChanged(bool value)
    {
        if (!value)
        {
            _lastOutlinePopupCloseAt = DateTime.UtcNow;
        }
    }

    [RelayCommand]
    private void SelectShape(ShapeOption option)
    {
        if (SelectedShape is not null)
        {
            SelectedShape.IsSelected = false;
        }

        option.IsSelected = true;
        SelectedShape = option;
    }

    /// <summary>
    /// Drive every setting from an existing shape item — used when the user opens the shape
    /// settings while a shape is selected so the popup reflects what's actually on the canvas.
    /// The cascade through <c>OnSelectedShapeStyleChanged</c> writes these values back to the
    /// same item; the <c>SetProperty</c> guards in <see cref="ShapeItem"/> short-circuit no-op
    /// writes. Kind is immutable, so the shape picker is synced only to keep the Fill pill's
    /// enabled state correct (and to seed the next-drawn shape) — it does not restyle the item.
    /// </summary>
    public void SyncFromShapeItem(ShapeItem item)
    {
        ShapeOption? match = Shapes.FirstOrDefault(s => s.Kind == item.Kind);
        if (match is not null && !ReferenceEquals(SelectedShape, match))
        {
            SelectShape(match);
        }

        FillSettings.SyncFromBrush(item.Fill);
        OutlineSettings.SyncFromBrush(item.Outline);
        OutlineSettings.Size = Math.Clamp(item.OutlineThickness, OutlineSettings.MinSize, OutlineSettings.MaxSize);
    }

    private static IEnumerable<ShapeOption> CreateShapes()
    {
        yield return new ShapeOption(ShapeKind.Square, "Square", Images.square.ToImageSource());
        yield return new ShapeOption(ShapeKind.Circle, "Circle", Images.circle.ToImageSource());
        yield return new ShapeOption(ShapeKind.Line, "Line", Images.line.ToImageSource());
        yield return new ShapeOption(ShapeKind.Arrow, "Arrow", Images.arrow.ToImageSource());
    }
}
