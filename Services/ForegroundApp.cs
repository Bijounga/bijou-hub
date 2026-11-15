using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BijouHub.Services;

// What's in front right now: the app's process name and the window's title. Used by the "still working?"
// nudge. Cheap enough to ask once a second.
public static class ForegroundApp
{
    public record Info(string Process, string Title);

    private static readonly HashSet<string> Shell = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "searchhost", "startmenuexperiencehost", "shellexperiencehost", "lockapp", "textinputhost",
    };

    private static uint _lastPid;
    private static string _lastName = "";

    // BIJOUHUB_DRIFT_FOREGROUND="process|window title" stands in for the real foreground window, so a
    // test can show what happens when a distraction is in front without taking over the desktop.
    private static readonly Info? Fake = Environment.GetEnvironmentVariable("BIJOUHUB_DRIFT_FOREGROUND") is { Length: > 0 } f
        ? new Info(f.Split('|')[0], f.Contains('|') ? f[(f.IndexOf('|') + 1)..] : "") : null;

    public static Info? Current()
    {
        if (Fake != null) return Fake;
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null; // BijouHub itself is never a distraction

        if (pid != _lastPid)
        {
            try { _lastName = Process.GetProcessById((int)pid).ProcessName; }
            catch { return null; }
            _lastPid = pid;
        }
        if (Shell.Contains(_lastName)) return null;

        var title = new StringBuilder(256);
        GetWindowText(window, title, title.Capacity);
        return new Info(_lastName, title.ToString());
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
}
