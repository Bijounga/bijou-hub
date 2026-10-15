using Microsoft.Win32;

namespace BijouHub.Services;

// Registers BijouHub to launch at Windows sign-in via the standard per-user Run key —
// no installer or scheduled task needed, and it's automatically removed if the user
// uninstalls by just deleting the exe (the registry value just points at a path).
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BijouHub";

    // Launched at sign-in: start in the tray when "keep running in the background" is on.
    public const string TrayArgument = "--tray";

    // Older versions registered the exe without the argument; add it, but only for this same exe
    // (a test build must never take over the user's startup entry).
    public static void UpgradeEntry()
    {
        if (TestMarker != null) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string value || value.Contains(TrayArgument)) return;
            if (!string.Equals(value.Trim().Trim('"'), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {TrayArgument}");
        }
        catch
        {
            // leave it as it was
        }
    }

    // A test instance (BIJOUHUB_DATA_DIR set) keeps this in a marker file: it must never touch the
    // real startup entry, which would then point at a test build.
    private static string? TestMarker =>
        Environment.GetEnvironmentVariable("BIJOUHUB_DATA_DIR") is { Length: > 0 } dir ? System.IO.Path.Combine(dir, "login_on") : null;

    public static bool IsEnabled
    {
        get
        {
            if (TestMarker is { } marker) return System.IO.File.Exists(marker);
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (TestMarker is { } marker)
        {
            if (enabled) System.IO.File.WriteAllText(marker, "");
            else System.IO.File.Delete(marker);
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;
            key.SetValue(ValueName, $"\"{exePath}\" {TrayArgument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
