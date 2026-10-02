using System.IO;

namespace BijouHub.Services;

// Splits storage into two roots: LocalDir always lives under %APPDATA%\BijouHub and
// holds machine-specific things (settings, work modes and their local launch paths,
// quick-launch app shortcuts, keybinds) that can never mean the same thing on another
// device. SyncDir holds the portable stuff (projects, goals, session/time history) and
// can be redirected into a folder the user already syncs across devices (Dropbox,
// Google Drive, OneDrive, iCloud Drive, ...) so that data follows them machine to
// machine without BijouHub needing its own sync backend.
public static class DataPaths
{
    // BIJOUHUB_DATA_DIR relocates everything (settings included, so the sync folder too) —
    // lets a test instance run against a scratch folder without touching real data.
    public static string LocalDir { get; } = EnsureDir(
        Environment.GetEnvironmentVariable("BIJOUHUB_DATA_DIR") is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BijouHub"));

    public static string SyncDir
    {
        get
        {
            var settings = new AppSettingsStore().Load();
            var dir = string.IsNullOrWhiteSpace(settings.DataFolderPath) ? LocalDir : settings.DataFolderPath;
            return EnsureDir(dir);
        }
    }

    private static string EnsureDir(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
