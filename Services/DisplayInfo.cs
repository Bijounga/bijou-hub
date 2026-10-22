using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BijouHub.Services;

// The connected monitors, in screen pixels, and putting a window exactly over one of them.
public static class DisplayInfo
{
    public readonly record struct Screen(int X, int Y, int Width, int Height, bool Primary);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    public static List<Screen> All()
    {
        var found = new List<Screen>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                found.Add(new Screen(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return found.OrderBy(s => s.X).ThenBy(s => s.Y).ToList();
    }

    // The monitor a window is mostly on.
    public static Screen? Of(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return null;
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info)
            ? new Screen(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, (info.Flags & 1) != 0)
            : null;
    }

    // Covers a monitor completely with the window, above the taskbar (the taskbar steps out of the way
    // of a window that fills its monitor). Applied again a moment later, since Windows rescales a window
    // that lands on a monitor with a different scale.
    public static void Cover(Window window, Screen screen, bool topmost)
    {
        void Place()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, topmost ? HwndTopmost : HwndNoTopmost, screen.X, screen.Y, screen.Width, screen.Height, SwpNoActivate | SwpShowWindow);
        }
        Place();
        var again = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        again.Tick += (_, _) => { again.Stop(); Place(); };
        again.Start();
    }
}
