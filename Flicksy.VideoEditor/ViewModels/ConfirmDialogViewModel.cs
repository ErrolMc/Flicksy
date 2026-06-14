using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flicksy.VideoEditor.ViewModels;

/// <summary>
/// Overlay content VM for a reusable yes/no confirmation tile (templated by
/// <see cref="Controls.OverlayHost"/>, view is <see cref="Controls.ConfirmDialogView"/>). The
/// header, body, and both button labels are caller-supplied, so one dialog type backs every
/// confirm in the editor (Delete track today; remove source, discard changes, … later). The
/// confirm button runs the injected <c>onConfirm</c> action; both buttons — plus the backdrop /
/// Esc light-dismiss the host provides — close through the injected <c>close</c> action, so the VM
/// never holds a host reference (mirrors <see cref="SettingsOverlayViewModel"/>). A dismissal
/// (Cancel / backdrop / Esc) is simply "don't run <c>onConfirm</c>"; there is no separate cancel
/// callback.
/// </summary>
public partial class ConfirmDialogViewModel : ObservableObject
{
    private readonly Action _onConfirm;
    private readonly Action _close;

    public ConfirmDialogViewModel(string header, string body, string confirmLabel, string cancelLabel, Action onConfirm, Action close)
    {
        Header = header;
        Body = body;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
        _onConfirm = onConfirm;
        _close = close;
    }

    public string Header { get; }

    public string Body { get; }

    public string ConfirmLabel { get; }

    public string CancelLabel { get; }

    [RelayCommand]
    private void Confirm()
    {
        // Clear the overlay before running the action, mirroring OverlayHostViewModel.Close (which
        // clears state before invoking its callback) so onConfirm is free to open a follow-up
        // overlay if a flow ever needs one.
        _close();
        _onConfirm();
    }

    [RelayCommand]
    private void Cancel()
    {
        _close();
    }
}
