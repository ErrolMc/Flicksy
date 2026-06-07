using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;

namespace Flicksy.VideoEditor.Windows;

public partial class WelcomeWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public WelcomeWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) 
            return;

        int useDark = 1;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, sizeof(int));
        }
    }

    private void OnNewVideoProjectClicked(object sender, RoutedEventArgs e)
    {
        var editor = App.Services.GetRequiredService<VideoEditorWindow>();

        // Hand window ownership to the editor so closing Welcome doesn't terminate the
        // app (App.ShutdownMode default is OnMainWindowClose).
        Application.Current.MainWindow = editor;
        editor.Show();
        Close();
    }
}
