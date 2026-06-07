using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flicksy.PostSnip.ViewModels;

public partial class PenColorOption : ObservableObject
{
    public PenColorOption(Brush brush)
    {
        Brush = brush;
    }

    public Brush Brush { get; }

    [ObservableProperty]
    private bool isSelected;
}

public partial class PenSettingsViewModel : ObservableObject
{
    public double MinSize { get; } = 1;

    public double MaxSize { get; } = 30;

    [ObservableProperty]
    private double size = 10;

    [ObservableProperty]
    private PenColorOption? selectedColor;

    public ObservableCollection<PenColorOption> Colors { get; }

    public PenSettingsViewModel()
    {
        Colors = new ObservableCollection<PenColorOption>(CreateColors());
        SelectColor(Colors[0]);
    }

    [RelayCommand]
    private void SelectColor(PenColorOption option)
    {
        if (SelectedColor is not null)
        {
            SelectedColor.IsSelected = false;
        }

        option.IsSelected = true;
        SelectedColor = option;
    }

    private static IEnumerable<PenColorOption> CreateColors()
    {
        string[] hexes =
        {
            "#000000", "#FFFFFF", "#D9D9D9", "#ACACAC", "#767676", "#595959",
            "#C2185B", "#E53935", "#FF6D00", "#FFA000", "#FFD600", "#FFEB3B",
            "#AEEA00", "#00C853", "#00695C", "#00B0FF", "#1976D2", "#1A237E",
            "#6200EA", "#4A148C", "#FFCCBC", "#BCAAA4", "#795548", "#5D4037",
            "#F48FB1", "#FFAB91", "#FFF59D", "#A5D6A7", "#81D4FA", "#B39DDB",
        };

        foreach (string hex in hexes)
        {
            Color color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            yield return new PenColorOption(brush);
        }
    }
}
