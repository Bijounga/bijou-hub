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
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Placement { public int Length, Flags, ShowCmd; public Point MinPosition, MaxPosition; public Rect Normal; }
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
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

    // Where a window is and whether it's maximized, in screen pixels, to put it back exactly later. (A
    // window's Left/Width are scaled units that mean different things on monitors with different scaling.)
    public readonly record struct Saved(object Placement);

    public static Saved? Save(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        return hwnd != IntPtr.Zero && GetWindowPlacement(hwnd, ref placement) ? new Saved(placement) : null;
    }

    public static void Restore(Window window, Saved saved)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || saved.Placement is not Placement placement) return;
        // Not minimized: it was on screen when F11 was pressed.
        if (placement.ShowCmd == 2) placement.ShowCmd = 1;
        SetWindowPlacement(hwnd, ref placement);
    }

    // Covers a monitor completely with the window, over the taskbar (a borderless maximized window does
    // that). The window must have no border (WindowStyle None).
    //
    // Onto another monitor, it lands there as a small window first and is maximized once Windows has
    // rescaled it for that monitor. Resizing it by hand while it crosses to a monitor with a different
    // scale ends with WPF's layout at twice the real size, the content spilling far past the monitor.
    public static void Cover(Window window, Screen screen, bool topmost)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var after = topmost ? HwndTopmost : HwndNoTopmost;
        void Maximize()
        {
            window.WindowState = WindowState.Maximized;
            SetWindowPos(handle, after, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var elsewhere = GetMonitorInfo(MonitorFromWindow(handle, 2), ref info) && (info.Monitor.Left != screen.X || info.Monitor.Top != screen.Y);
        if (window.WindowState == WindowState.Maximized && !elsewhere) return;
        window.WindowState = WindowState.Normal;
        if (!elsewhere) { Maximize(); return; }

        var done = false;
        void Settled()
        {
            if (done) return;
            done = true;
            window.DpiChanged -= OnDpiChanged;
            window.Dispatcher.BeginInvoke(Maximize, System.Windows.Threading.DispatcherPriority.Background);
        }
        void OnDpiChanged(object? sender, DpiChangedEventArgs e) => Settled();
        window.DpiChanged += OnDpiChanged;
        SetWindowPos(handle, after, screen.X + 20, screen.Y + 20, 200, 120, SwpNoActivate | SwpShowWindow);
        // (no such message when both monitors have the same scale)
        var fallback = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        fallback.Tick += (_, _) => { fallback.Stop(); Settled(); };
        fallback.Start();
    }
}
