using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using BijouHub.Services;

namespace BijouHub.Mac.Services;

// Puts a widget window back where it was left, on whichever monitor that was, and remembers where
// it's moved to. A saved spot that's no longer on any connected screen is ignored. Same behavior as Windows.
public static class WidgetPlacement
{
    [DllImport("/usr/lib/libobjc.dylib")] private static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] private static extern void MsgSendNInt(IntPtr receiver, IntPtr selector, nint argument);

    // Widgets either float on top, or sit on the desktop: below every normal window but above the
    // wallpaper and desktop icons (and still there when you push the windows aside). On a Mac that's
    // the window's level; elsewhere (a test run) it's just "not topmost".
    public static void Layer(Window window, Func<bool> onTop)
    {
        void Apply()
        {
            var top = onTop();
            window.Topmost = top;
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                if (window.TryGetPlatformHandle() is { } handle && handle.HandleDescriptor == "NSWindow")
                {
                    // 0 is a normal window, 3 a floating one; -1 is just under normal windows.
                    MsgSendNInt(handle.Handle, sel_registerName("setLevel:"), top ? 3 : -1);
                    // On every Space, so it's there on whichever desktop is showing.
                    MsgSendNInt(handle.Handle, sel_registerName("setCollectionBehavior:"), top ? 0 : 1);
                }
            }
            catch
            {
                // no native layering available: it stays an ordinary window
            }
        }
        window.Opened += (_, _) => Apply();
        window.Tag = (Action)Apply;
    }

    // After the setting changed.
    public static void Reapply(Window? window) => (window?.Tag as Action)?.Invoke();

    public static bool OnScreen(Screens? screens, PixelPoint at) =>
        screens == null || screens.All.Any(s => s.Bounds.Contains(new PixelPoint(at.X + 20, at.Y + 20)));

    // Call before the window is shown: restores the saved place, and saves it back (a moment after
    // the last move) through `save`. Returns whether a saved position was applied.
    public static bool Attach(Window window, WidgetSettings saved, bool restoreSize, Func<Screens?> screens, Action<WidgetSettings> save)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        if (restoreSize && saved.Width is { } w && saved.Height is { } h && w >= window.MinWidth && h >= window.MinHeight)
        {
            window.Width = w;
            window.Height = h;
        }
        var positioned = false;
        if (saved.X is { } x && saved.Y is { } y && OnScreen(screens(), new PixelPoint(x, y)))
        {
            window.Position = new PixelPoint(x, y);
            positioned = true;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            saved.X = window.Position.X;
            saved.Y = window.Position.Y;
            if (restoreSize)
            {
                saved.Width = window.Bounds.Width;
                saved.Height = window.Bounds.Height;
            }
            save(saved);
        };
        void Changed() { timer.Stop(); timer.Start(); }
        window.PositionChanged += (_, _) => Changed();
        window.Resized += (_, _) => { if (restoreSize) Changed(); };
        window.Closing += (_, _) =>
        {
            if (!timer.IsEnabled) return;
            timer.Stop();
            saved.X = window.Position.X;
            saved.Y = window.Position.Y;
            save(saved);
        };
        return positioned;
    }
}
