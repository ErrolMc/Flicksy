using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Helpers;
using Flicksy.Drawing.Undo;
using Flicksy.VideoEditor.Services;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Backs the File / Edit menus and the caption-area gear button on
/// <see cref="Controls.TitleBarView"/>. The Edit menu's Undo/Redo items bind through
/// <see cref="History"/> (the editor's shared <see cref="UndoManager"/>), so they
/// enable/disable with the undo stacks. Project Settings (File menu) and Settings (gear button)
/// open overlays through the injected <see cref="IOverlayService"/>, reading the document's
/// settings through <see cref="IProjectSettingsService"/> — this VM never holds a root-VM
/// reference. Every other command is an intentionally empty placeholder so the menu structure
/// exists ahead of the features; export, save/load and clipboard wiring replace the bodies as
/// those slices land.
/// </summary>
public partial class TitleBarViewModel : ObservableObject
{
    private readonly IOverlayService _overlay;
    private readonly IProjectSettingsService _projectSettings;

    public UndoManager History { get; }

    /// <summary>The gear glyph (white-on-transparent) for the caption-area Settings button.</summary>
    public ImageSource SettingsIcon { get; } = Images.settings.ToImageSource();

    public TitleBarViewModel(UndoManager history, IOverlayService overlay, IProjectSettingsService projectSettings)
    {
        History = history;
        _overlay = overlay;
        _projectSettings = projectSettings;
    }

    [RelayCommand]
    private void Export()
    {
    }

    [RelayCommand]
    private void SaveProject()
    {
    }

    [RelayCommand]
    private void SaveProjectAs()
    {
    }

    [RelayCommand]
    private void ProjectSettings()
    {
        _overlay.Show(new ProjectSettingsOverlayViewModel(_projectSettings.Current, _overlay.Close));
    }

    [RelayCommand]
    private void Settings()
    {
        _overlay.Show(new SettingsOverlayViewModel(_overlay.Close));
    }

    [RelayCommand]
    private void Copy()
    {
    }

    [RelayCommand]
    private void Cut()
    {
    }

    [RelayCommand]
    private void Paste()
    {
    }
}
