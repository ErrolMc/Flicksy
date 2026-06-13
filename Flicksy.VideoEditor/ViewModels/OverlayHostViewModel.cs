using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Flicksy.VideoEditor.Services;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// The shell's modal overlay layer — a reusable host for one centered tile at a time
/// (Project Settings, Settings, future Export). The bindable implementation of
/// <see cref="Services.IOverlayService"/>: <see cref="Controls.OverlayHost"/> binds
/// <see cref="CurrentOverlay"/> and templates it into the matching tile view, and any VM
/// can open an overlay through the service via <see cref="Show"/>/<see cref="Close"/>.
///
/// One overlay at a time: <see cref="Show"/> over an open overlay replaces it (the
/// outgoing overlay's callback fires first). <c>onClosed</c> fires exactly once, on
/// every close path — explicit <see cref="Close"/>, backdrop click, or Esc — after the
/// overlay state is already cleared, so a callback may immediately <see cref="Show"/>
/// the next overlay of a flow. <c>allowLightDismiss=false</c> reserves backdrop/Esc for
/// overlays that must not be casually dismissed (e.g. a running export); explicit
/// <see cref="Close"/> still works. UI-thread only, like the rest of the shell state.
/// </summary>
public partial class OverlayHostViewModel : ObservableObject, IOverlayService
{
    private Action? _onClosed;
    private bool _allowLightDismiss;

    /// <summary>Content VM of the overlay currently shown; null when none is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayOpen))]
    private object? currentOverlay;

    public bool IsOverlayOpen => CurrentOverlay is not null;

    public void Show(object overlayViewModel, Action? onClosed = null, bool allowLightDismiss = true)
    {
        ArgumentNullException.ThrowIfNull(overlayViewModel);

        Close();
        _onClosed = onClosed;
        _allowLightDismiss = allowLightDismiss;
        CurrentOverlay = overlayViewModel;
    }

    public void Close()
    {
        if (CurrentOverlay is null)
            return;

        CurrentOverlay = null;
        Action? onClosed = _onClosed;
        _onClosed = null;
        onClosed?.Invoke();
    }

    /// <summary>
    /// Close on behalf of a backdrop click or Esc. Returns false (and keeps the overlay
    /// open) when the overlay was shown with <c>allowLightDismiss=false</c>.
    /// </summary>
    public bool TryLightDismiss()
    {
        if (CurrentOverlay is null || !_allowLightDismiss)
            return false;

        Close();
        return true;
    }
}
