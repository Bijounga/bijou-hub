using System.IO;
using Microsoft.Win32;

namespace BijouHub.Services;

// Silences Windows notifications while a "Do Not Disturb" mode runs: the per-user switch behind
// Settings > System > Notifications > Do not disturb (the same value Microsoft's own
// WindowsDeveloperConfig sets). Only undone if BijouHub was the one that turned it on, so a
// Do Not Disturb the user set themselves is left alone; a marker file covers a crash in between.
public static class DoNotDisturb
{
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string ValueName = "NOC_GLOBAL_SETTING_TOASTS_ENABLED";

    // BIJOUHUB_DND_KEY points tests at a scratch key instead of the real setting.
    private static string KeyPath => Environment.GetEnvironmentVariable("BIJOUHUB_DND_KEY") is { Length: > 0 } test ? test : SettingsKey;

    private static string MarkerPath => Path.Combine(DataPaths.LocalDir, "dnd_on");

    public static bool TurnedOnByUs => File.Exists(MarkerPath);

    public static void TurnOn()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (key.GetValue(ValueName) is int enabled && enabled == 0) return; // already on, the user's doing
            File.WriteAllText(MarkerPath, "");
            key.SetValue(ValueName, 0, RegistryValueKind.DWord);
        }
        catch
        {
            // Notifications just stay on.
        }
    }

    public static void Restore()
    {
        if (!TurnedOnByUs) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(ValueName, 1, RegistryValueKind.DWord);
            File.Delete(MarkerPath);
        }
        catch
        {
            // Left for the next launch to retry.
        }
    }
}
