using System.Diagnostics;
using BijouHub.Services;

namespace BijouHub.Mac.Services;

// Do Not Disturb for "Do Not Disturb" modes. macOS has no API for apps to set a Focus, but
// Shortcuts can: BijouHub runs two shortcuts the user makes once, "BijouHub Focus On" and
// "BijouHub Focus Off" (each a single Set Focus action). A marker file remembers that BijouHub
// turned it on, so a crash mid-session still turns it off at the next launch.
public static class MacDoNotDisturb
{
    public const string OnShortcut = "BijouHub Focus On";
    public const string OffShortcut = "BijouHub Focus Off";

    private static string MarkerPath => Path.Combine(DataPaths.LocalDir, "dnd_on");

    public static void TurnOn()
    {
        try { File.WriteAllText(MarkerPath, ""); }
        catch { /* worst case it isn't turned off after a crash */ }
        RunShortcut(OnShortcut);
    }

    public static void Restore()
    {
        if (!File.Exists(MarkerPath)) return;
        RunShortcut(OffShortcut);
        try { File.Delete(MarkerPath); }
        catch { /* retried next launch */ }
    }

    private static void RunShortcut(string name)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var start = new ProcessStartInfo("/usr/bin/shortcuts") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("run");
            start.ArgumentList.Add(name);
            Process.Start(start); // fire and forget: a missing shortcut just does nothing
        }
        catch
        {
            // No Shortcuts app (or no such shortcut): notifications stay as they are.
        }
    }
}
