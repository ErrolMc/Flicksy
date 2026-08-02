using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Helpers;

namespace Flicksy.Drawing.ViewModels;

public partial class OutlineColorOption : ObservableObject
{
    public OutlineColorOption(Brush brush, bool isNone = false)
    {
        Brush = brush;
        IsNone = isNone;
    }

    public Brush Brush { get; }

    public bool IsNone { get; }

    [ObservableProperty]
    private bool isSelected;
}

public partial class OutlineSettingsViewModel : ObservableObject
{
    public double MinSize { get; } = 1;

    public double MaxSize { get; } = 30;

    [ObservableProperty]
    private double size = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveBrush))]
    private double opacity = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveBrush))]
    private OutlineColorOption? selectedColor;

    public Brush? EffectiveBrush
    {
        get
        {
            if (SelectedColor is null || SelectedColor.IsNone)
            {
                return null;
            }

            if (SelectedColor.Brush is not SolidColorBrush solid)
            {
                return SelectedColor.Brush;
            }

            Color c = solid.Color;
            byte alpha = (byte)Math.Clamp((int)Math.Round(c.A * Opacity), 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }
    }

    public ObservableCollection<OutlineColorOption> Colors { get; }

    public ImageSource CrossCircle { get; } = Images.cross_circle.ToImageSource();

    public OutlineSettingsViewModel()
    {
        Colors = new ObservableCollection<OutlineColorOption>(CreateColors());
        SelectColor(Colors[1]);
    }

    [RelayCommand]
    private void SelectColor(OutlineColorOption option)
    {
        if (SelectedColor is not null)
        {
            SelectedColor.IsSelected = false;
        }

        option.IsSelected = true;
        SelectedColor = option;
    }

    /// <summary>
    /// Drive the selected swatch + opacity from an existing brush. Null is treated as
    /// "none". Unmatched brushes leave the VM unchanged.
    /// </summary>
    public void SyncFromBrush(Brush? brush)
    {
        if (brush is null)
        {
            OutlineColorOption? noneOption = Colors.FirstOrDefault(o => o.IsNone);
            if (noneOption is not null && !ReferenceEquals(SelectedColor, noneOption))
            {
                SelectColor(noneOption);
            }
            return;
        }

        if (brush is not SolidColorBrush solid)
        {
            return;
        }

        Color target = solid.Color;
        foreach (OutlineColorOption option in Colors)
        {
            if (option.IsNone || option.Brush is not SolidColorBrush swatch)
            {
                continue;
            }
            Color s = swatch.Color;
            if (s.R != target.R || s.G != target.G || s.B != target.B)
            {
                continue;
            }

            if (!ReferenceEquals(SelectedColor, option))
            {
                SelectColor(option);
            }
            Opacity = s.A == 0 ? 1d : Math.Clamp(target.A / (double)s.A, 0d, 1d);
            return;
        }
    }

    private static IEnumerable<OutlineColorOption> CreateColors()
    {
        yield return new OutlineColorOption(CreateBrush("#FFFFFF"), isNone: true);

        string[] hexes =
        {
            "#000000", "#FFFFFF", "#D9D9D9", "#ACACAC", "#767676",
            "#C2185B", "#E53935", "#FF6D00", "#FFA000", "#FFD600", "#FFEB3B",
            "#AEEA00", "#00C853", "#00695C", "#00B0FF", "#1976D2", "#1A237E",
            "#6200EA", "#4A148C", "#FFCCBC", "#BCAAA4", "#795548", "#5D4037",
            "#F48FB1", "#FFAB91", "#FFF59D", "#A5D6A7", "#81D4FA", "#B39DDB",
        };

        foreach (string hex in hexes)
        {
            yield return new OutlineColorOption(CreateBrush(hex));
        }
    }

    private static Brush CreateBrush(string hex)
    {
        Color color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
