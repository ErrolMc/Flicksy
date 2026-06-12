using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.VideoEditor.Project;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Overlay content VM for the Project Settings tile (templated by
/// <see cref="Controls.OverlayHost"/>, view is <see cref="Controls.ProjectSettingsOverlay"/>).
/// Exposes the document's <see cref="ProjectSettings"/> for display — read-only this
/// slice; editing lands later. Close is an injected <see cref="Action"/> (the root VM
/// passes <c>OverlayHost.Close</c>) so content VMs never hold a host reference.
/// </summary>
public partial class ProjectSettingsOverlayViewModel : ObservableObject
{
    private readonly Action _close;

    public ProjectSettings Settings { get; }

    public ProjectSettingsOverlayViewModel(ProjectSettings settings, Action close)
    {
        Settings = settings;
        _close = close;
    }

    [RelayCommand]
    private void Close()
    {
        _close();
    }
}
