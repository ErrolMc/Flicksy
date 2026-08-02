using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Helpers;
using Flicksy.Drawing.Source;

namespace Flicksy.Drawing.ViewModels;

public partial class TextSettingsViewModel : ObservableObject
{
    private DateTime _lastFillPopupCloseAt = DateTime.MinValue;
    private DateTime _lastOutlinePopupCloseAt = DateTime.MinValue;

    [ObservableProperty]
    private string selectedFont = "Segoe UI";

    [ObservableProperty]
    private double fontSize = 24;

    [ObservableProperty]
    private bool isFillSettingsOpen;

    [ObservableProperty]
    private bool isOutlineSettingsOpen;

    public double MinFontSize { get; } = 1;

    public double MaxFontSize { get; } = 512;

    public FillSettingsViewModel FillSettings { get; } = new();

    public OutlineSettingsViewModel OutlineSettings { get; } = new();

    public ObservableCollection<string> Fonts { get; }

    public ImageSource CircleOutside { get; } = Images.circle.ToImageSource();

    public ImageSource CircleInside { get; } = Images.circle_inside.ToImageSource();

    public ImageSource DonutOutside { get; } = Images.donut_outside.ToImageSource();

    public ImageSource DonutInside { get; } = Images.donut_inside.ToImageSource();

    public TextSettingsViewModel()
    {
        Fonts = new ObservableCollection<string>(CreateFonts());

        // Default fill to black (index 1 is the first solid colour, "#000000").
        FillSettings.SelectColorCommand.Execute(FillSettings.Colors[1]);

        // Default outline to none (index 0 is the "none" option).
        OutlineSettings.SelectColorCommand.Execute(OutlineSettings.Colors[0]);
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

    /// <summary>
    /// Drive every setting from an existing text item — used when the user selects a text
    /// item so the settings popup reflects what's actually on the canvas. The cascade
    /// through <c>OnSelectedTextStyleChanged</c> writes these values back to the same
    /// item; the <c>SetProperty</c> guards in <see cref="TextItem"/> short-circuit no-op
    /// writes so it doesn't churn geometry.
    /// </summary>
    public void SyncFromTextItem(TextItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.FontFamily))
        {
            SelectedFont = item.FontFamily;
        }
        FontSize = Math.Clamp(item.FontSize, MinFontSize, MaxFontSize);
        FillSettings.SyncFromBrush(item.Fill);
        OutlineSettings.SyncFromBrush(item.Outline);
        OutlineSettings.Size = Math.Clamp(item.OutlineThickness, OutlineSettings.MinSize, OutlineSettings.MaxSize);
    }

    private static IEnumerable<string> CreateFonts()
    {
        yield return "Segoe UI";
        yield return "Arial";
        yield return "Calibri";
        yield return "Cambria";
        yield return "Comic Sans MS";
        yield return "Consolas";
        yield return "Courier New";
        yield return "Georgia";
        yield return "Impact";
        yield return "Tahoma";
        yield return "Times New Roman";
        yield return "Trebuchet MS";
        yield return "Verdana";
    }
}
