using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Overlay content VM for the editor's one reusable message tile (templated by
/// <see cref="Controls.OverlayHost"/>, view is <see cref="Controls.ConfirmDialogView"/>). The
/// header, body, and button labels are caller-supplied, so one dialog type backs every non-native
/// prompt in the editor — two-button confirms (Delete track, remove source, relocate over a
/// duration change) and single-button info / error acknowledgements (import / relocate failed,
/// already imported). Pass a <c>cancelLabel</c> + <c>onConfirm</c> for a confirm; omit both for an
/// acknowledgement (one button that just dismisses — <see cref="HasCancel"/> is then false and the
/// view collapses the Cancel button).
///
/// The confirm button runs the injected <c>onConfirm</c> (if any); both buttons — plus the
/// backdrop / Esc light-dismiss the host provides — close through the injected <c>close</c> action,
/// so the VM never holds a host reference (mirrors <see cref="SettingsOverlayViewModel"/>). A
/// dismissal (Cancel / backdrop / Esc) is simply "don't run <c>onConfirm</c>"; there is no separate
/// cancel callback.
/// </summary>
public partial class ConfirmDialogViewModel : ObservableObject
{
    private readonly Action? _onConfirm;
    private readonly Action _close;

    /// <summary>Two-button confirm: Confirm runs <paramref name="onConfirm"/>; Cancel / backdrop / Esc just dismiss.</summary>
    public ConfirmDialogViewModel(string header, string body, string confirmLabel, string cancelLabel, Action onConfirm, Action close)
    {
        Header = header;
        Body = body;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
        _onConfirm = onConfirm;
        _close = close;
    }

    /// <summary>Single-button acknowledgement (info / error): the one button just dismisses — no Cancel, no action.</summary>
    public ConfirmDialogViewModel(string header, string body, string confirmLabel, Action close)
    {
        Header = header;
        Body = body;
        ConfirmLabel = confirmLabel;
        CancelLabel = null;
        _onConfirm = null;
        _close = close;
    }

    public string Header { get; }

    public string Body { get; }

    public string ConfirmLabel { get; }

    public string? CancelLabel { get; }

    /// <summary>False for a single-button acknowledgement — the view collapses the Cancel button.</summary>
    public bool HasCancel => CancelLabel is not null;

    [RelayCommand]
    private void Confirm()
    {
        // Clear the overlay before running the action, mirroring OverlayHostViewModel.Close (which
        // clears state before invoking its callback) so onConfirm is free to open a follow-up
        // overlay if a flow ever needs one.
        _close();
        _onConfirm?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        _close();
    }
}
