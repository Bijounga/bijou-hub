namespace BijouHub.Models;

public class WorkMode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Mode";
    public List<LaunchItem> LaunchItems { get; set; } = new();
    public List<BlockItem> BlockItems { get; set; } = new();

    // Saved countdown lengths, in minutes, shown as one-click timers on the mode's page.
    public List<int> TimerMinutes { get; set; } = new();

    // Saved Pomodoro cycles as typed, "25/5" (focus/break minutes).
    public List<string> PomodoroTimers { get; set; } = new();

    // Silences notifications while a session in this mode runs.
    public bool DoNotDisturb { get; set; }

    // Switches on the Elgato Key Light while a session in this mode runs.
    public bool KeyLight { get; set; }

    // The playlists this mode starts with (null: none): one while working, one on a Pomodoro break.
    public string? WorkPlaylistId { get; set; }
    public string? BreakPlaylistId { get; set; }

    // Time in a study mode is logged like any other, but stays out of the daily target and the
    // day and week totals that sit beside it.
    public bool IsStudy { get; set; }

    public override string ToString() => Name;
}
