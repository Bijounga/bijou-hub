using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BijouHub.Services;

namespace BijouHub.Services;

// Puts a widget window back where it was left, on whichever monitor that was, and remembers where
// it's moved to. Positions are screen pixels (so they mean the same on monitors with different
// scaling); a saved spot that's no longer on any connected monitor is ignored.
public static class WidgetPlacement
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    private const uint GwHwndNext = 2;
    private const uint GwHwndPrev = 3;

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int max);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNull = 0;

    // The desktop is itself a window (Progman, and the WorkerW windows that can hold the icons) at the
    // very bottom of the stack; a widget put under it would be hidden by the wallpaper.
    private static bool IsDesktopWindow(IntPtr hwnd)
    {
        var name = new System.Text.StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW";
    }

    // Sends the window behind every other window, but still in front of the desktop itself. (The
    // desktop isn't at the bottom of the stack: plenty of hidden helper windows are beneath it, so
    // "the bottom" would be behind the wallpaper and invisible.)
    public static void SendToBottom(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // The highest desktop window, from the top of the stack down.
        var desktop = GetTopWindow(IntPtr.Zero);
        while (desktop != IntPtr.Zero && !IsDesktopWindow(desktop)) desktop = GetWindow(desktop, GwHwndNext);
        if (desktop == IntPtr.Zero) return; // no desktop window found: leave it where it is

        // Sit directly above it: after the window that is just above the desktop (HWND_TOP if none).
        var above = GetWindow(desktop, GwHwndPrev);
        if (above == hwnd) return; // already there
        SetWindowPos(hwnd, above, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    // Widgets either float on top, or sit on the desktop: behind everything, coming forward while
    // they're used (clicked) and sinking again when something else is. `onTop` is read each time.
    public static void Layer(Window window, Func<bool> onTop)
    {
        void Apply()
        {
            var top = onTop();
            window.Topmost = top;
            if (!top) SendToBottom(window);
        }
        window.SourceInitialized += (_, _) => Apply();
        window.IsVisibleChanged += (_, _) => { if (window.IsVisible) Apply(); };
        window.Deactivated += (_, _) => { if (!onTop()) SendToBottom(window); };
        window.Tag = (Action)Apply;
    }

    // After the setting changed.
    public static void Reapply(Window? window) => (window?.Tag as Action)?.Invoke();

    // The window's top-left corner in screen pixels, once it has a handle.
    public static (int X, int Y)? Position(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r) ? (r.Left, r.Top) : null;
    }

    // Moves a window (that has a handle) to a pixel position, if that's on a connected monitor.
    public static bool MoveTo(Window window, int x, int y)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !OnAMonitor(x + 20, y + 20)) return false;
        return SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private static bool OnAMonitor(int x, int y) => MonitorFromPoint(new Point { X = x, Y = y }, MonitorDefaultToNull) != IntPtr.Zero;

    // Restores the saved place when the window gets its handle (before it's first shown), and saves
    // it back (a moment after the last move) through `save`.
    public static void Attach(Window window, WidgetSettings saved, bool restoreSize, Action<WidgetSettings> save)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (restoreSize && saved.Width is { } w && saved.Height is { } h && w >= window.MinWidth && h >= window.MinHeight)
            {
                window.Width = w;
                window.Height = h;
            }
            if (saved.X is { } x && saved.Y is { } y) MoveTo(window, x, y);
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Position(window) is not { } at) return;
            saved.X = at.X;
            saved.Y = at.Y;
            if (restoreSize && window.WindowState == WindowState.Normal)
            {
                saved.Width = window.ActualWidth;
                saved.Height = window.ActualHeight;
            }
            save(saved);
        };
        void Changed(object? s, EventArgs e) { timer.Stop(); timer.Start(); }
        window.LocationChanged += Changed;
        window.SizeChanged += (s, e) => Changed(s, e);
        window.Closing += (_, _) =>
        {
            // The last move before closing still counts (the handle is still there at this point).
            if (!timer.IsEnabled) return;
            timer.Stop();
            if (Position(window) is { } at) { saved.X = at.X; saved.Y = at.Y; save(saved); }
        };
    }
}
