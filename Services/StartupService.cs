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

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
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
