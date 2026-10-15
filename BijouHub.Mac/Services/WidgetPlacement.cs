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
