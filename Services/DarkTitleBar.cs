using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BijouHub.Services;

public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;
    private const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);

    // Light or dark caption to match the theme; on Windows 11 the theme can also tint the
    // caption itself (TitleBarColor / TitleBarTextColor resources — e.g. MS-DOS's navy bar).
    // Older Windows ignores the color attributes.
    public static void Apply(Window window)
    {
        void Set()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int useDark = ThemeService.IsLight(ThemeService.CurrentThemeName) ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));

            int caption = ColorRef(Application.Current.TryFindResource("TitleBarColor"));
            int text = ColorRef(Application.Current.TryFindResource("TitleBarTextColor"));
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
        }

        if (window.IsLoaded && new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Set();
        else
            window.SourceInitialized += (_, _) => Set();
    }

    // COLORREF is 0x00BBGGRR; a theme without the resource gets the system default back.
    private static int ColorRef(object? resource) =>
        resource is System.Windows.Media.Color c ? c.R | (c.G << 8) | (c.B << 16) : DWMWA_COLOR_DEFAULT;
}
