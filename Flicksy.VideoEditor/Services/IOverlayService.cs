using System;

namespace Flicksy.VideoEditor.Services;

/// <summary>
/// The shell's modal overlay layer — shows one centered tile at a time (Project Settings,
/// Settings, future Export) over a dim backdrop. Lets any VM open an overlay without a
/// reference to the root VM. Implemented by the bindable
/// <see cref="ViewModels.OverlayHostViewModel"/>, which the OverlayHost control binds.
/// </summary>
public interface IOverlayService
{
    /// <summary>Content VM of the overlay currently shown; null when none is open.</summary>
    object? CurrentOverlay { get; }

    bool IsOverlayOpen { get; }

    /// <summary>
    /// Shows <paramref name="overlayViewModel"/>, replacing any open overlay (the outgoing
    /// overlay's callback fires first). <paramref name="onClosed"/> fires exactly once on
    /// every close path — explicit <see cref="Close"/>, backdrop click, or Esc.
    /// <paramref name="allowLightDismiss"/> = false reserves backdrop/Esc for overlays that
    /// must not be casually dismissed (e.g. a running export); <see cref="Close"/> still works.
    /// </summary>
    void Show(object overlayViewModel, Action? onClosed = null, bool allowLightDismiss = true);

    void Close();

    /// <summary>
    /// Close on behalf of a backdrop click or Esc. Returns false (and keeps the overlay open)
    /// when it was shown with <c>allowLightDismiss=false</c>.
    /// </summary>
    bool TryLightDismiss();
}
