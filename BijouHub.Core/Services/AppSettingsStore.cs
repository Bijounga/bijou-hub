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

    // Sidebar channel sections folded shut (channel ids; "" is the No channel section).
    public List<string> CollapsedChannels { get; set; } = new();

    // The Home board is showing just this channel ("" for all, "none" for projects without one).
    public string BoardChannelFilter { get; set; } = "";

    // Elgato Key Light address ("192.168.1.40", optionally with :port); null when none is set up.
    public string? KeyLightAddress { get; set; }

    // Switch the light off again when the session that lit it ends.
    public bool KeyLightOffWhenDone { get; set; } = true;

    public WidgetSettings TaskWidget { get; set; } = new();
    public WidgetSettings TimerWidget { get; set; } = new();

    // What the start-timer widget last had picked (project id, mode id, minutes; 0 for open-ended).
    public string? WidgetProjectId { get; set; }
    public string? WidgetModeId { get; set; }
    public int WidgetMinutes { get; set; }

    // The one-time "still running in the hidden icons" notice has been shown.
    public bool TrayHintShown { get; set; }
}

// A desktop widget (the task list, the start-timer): whether it comes back when BijouHub starts, and
// where the user left it (screen pixels for the position; the size in the window's own units), so a
// widget kept on a second monitor is there again next time. Per computer: this file isn't synced.
public class WidgetSettings
{
    public bool ShowAtStartup { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
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
