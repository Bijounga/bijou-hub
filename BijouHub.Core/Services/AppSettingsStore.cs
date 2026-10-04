using System.IO;
using System.Text.Json;

namespace BijouHub.Services;

public class AppSettings
{
    public double ZoomLevel { get; set; } = 1.0;

    // Null/empty means "use the default local folder". When set, points at a folder
    // the user syncs across devices (Dropbox, Google Drive, OneDrive, iCloud Drive, ...)
    // so Projects and session history follow them between machines.
    public string? DataFolderPath { get; set; }

    public string ThemeName { get; set; } = "Dark";

    // The home goal list's selected tab (a Google Tasks list group, or "*" for all).
    public string GoalScope { get; set; } = "EDITING";

    // How much to work each day, for the ring on Home and the Daily Target key. Null: no target.
    public int? DailyTargetMinutes { get; set; }

    // Closing the window hides it to the notification area instead, so the Stream Deck keys
    // keep working.
    public bool KeepRunningWhenClosed { get; set; }

    // Finished tasks are shown (under the list) rather than tucked away.
    public bool ShowCompletedGoals { get; set; }

    // The one-time "still running in the hidden icons" notice has been shown.
    public bool TrayHintShown { get; set; }
}

public class AppSettingsStore
{
    private readonly string _filePath;

    public AppSettingsStore()
    {
        var dir = DataPaths.LocalDir;
        _filePath = System.IO.Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
