using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BijouHub.Services;

// Putting a window in front when the user did something somewhere else (a Stream Deck key, the
// tray): Windows only lets the program that has the user's input take the foreground, and a key
// press on the deck goes to the Stream Deck software, not to BijouHub. Joining the input queue of
// whatever is in front for a moment makes Windows treat BijouHub as part of it.
public static class ForegroundWindow
{
    public static void Raise(Window window)
    {
        if (!window.IsVisible) window.Show();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        if (IsIconic(handle)) ShowWindow(handle, SwRestore); // comes back as it was (maximized stays maximized)

        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        var ownThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
        }
        finally
        {
            if (attached) AttachThreadInput(ownThread, foregroundThread, false);
        }

        // Still not in front (the thing in front wouldn't share): the taskbar's own way of switching.
        if (GetForegroundWindow() != handle) SwitchToThisWindow(handle, true);
        window.Activate();
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr window, bool altTab);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
}
