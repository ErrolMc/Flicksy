using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Flicksy.Agent;

internal sealed class HotKeyWindow : NativeWindow, IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int HotKeyId = 1;
    private readonly Action _onPressed;

    public HotKeyWindow(Action onPressed)
    {
        _onPressed = onPressed;
        CreateHandle(new CreateParams());

        const KeyModifiers modifiers = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt;
        bool registered = RegisterHotKey(Handle, HotKeyId, (uint)modifiers, (uint)Keys.S);
        if (!registered)
        {
            throw new InvalidOperationException("Failed to register Ctrl+Shift+Alt+S global hotkey.");
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotKey && m.WParam == (IntPtr)HotKeyId)
        {
            _onPressed();
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, HotKeyId);
        DestroyHandle();
        GC.SuppressFinalize(this);
    }

    [Flags]
    private enum KeyModifiers : uint
    {
        Alt = 0x1,
        Control = 0x2,
        Shift = 0x4
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
