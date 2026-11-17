namespace BijouHub.Models;

public enum PaceRhythm { Daily, Weekdays, Weekly }

// How much time to put into a task each day (or each week): the task itself can take days or weeks,
// this is the steady step toward it. Kept apart from the task so it survives Google Tasks refreshes.
public class TaskPace
{
    // DailyGoal.StableKey of the task: its Google Tasks id, or its own id when it isn't on Google.
    public string Key { get; set; } = "";

    // The task's words and project as they were when the pace was set, so a pace can still be shown
    // (and removed) if the task has since been finished or deleted.
    public string Text { get; set; } = "";
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }

    public int Minutes { get; set; } = 60;
    public PaceRhythm Rhythm { get; set; } = PaceRhythm.Daily;
    public DateTime CreatedOn { get; set; } = DateTime.Today;

    public string RhythmWord => Rhythm switch { PaceRhythm.Weekly => "week", _ => "day" };
}
