using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BijouHub.Services;

public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void Apply(Window window)
    {
        void Set()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }

        if (window.IsLoaded && new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Set();
        else
            window.SourceInitialized += (_, _) => Set();
    }
}
