using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Undo;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Backs the File / Edit menus on <see cref="Controls.TitleBarView"/>. The Edit menu's
/// Undo/Redo items bind through <see cref="History"/> (the editor's shared
/// <see cref="UndoManager"/>), so they enable/disable with the undo stacks. Every other
/// command is an intentionally empty placeholder so the menu structure exists ahead of
/// the features; export, save/load, project settings and clipboard wiring replace the
/// bodies as those slices land.
/// </summary>
public partial class TitleBarViewModel : ObservableObject
{
    public UndoManager History { get; }

    public TitleBarViewModel(UndoManager history)
    {
        History = history;
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
