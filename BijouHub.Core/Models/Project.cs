using System.Collections.ObjectModel;

namespace BijouHub.Models;

public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Project";

    // What screen readers announce for the project's row.
    public override string ToString() => Name;
    public string? LinkedModeId { get; set; }

    // The channel this project belongs to (see Channel); null for none.
    public string? ChannelId { get; set; }

    // The project being worked on right now in its channel: starred in the sidebar, first in its
    // section, badged on the board. At most one per channel (see ChannelRows.SetMain).
    public bool IsMain { get; set; }

    // UI-only: the channel's name and color, stamped on by ChannelRows.Apply.
    [System.Text.Json.Serialization.JsonIgnore] public string? ChannelName { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? ChannelColor { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool HasChannel => ChannelColor != null;
    public ObservableCollection<Goal> Goals { get; set; } = new();
    public List<ProjectNote> Notes { get; set; } = new();
    public int? DefaultTargetMinutes { get; set; }

    // When the project is due (yyyy-MM-dd; see DueText), shown on its board card. Without one the
    // card uses the due date on the project's BijouDocs script, if it has one.
    public string? DueDate { get; set; }
    public string? FreeformNotesXaml { get; set; }

    // Plain-text mirror of the freeform notes, kept in sync by both apps so notes carry over
    // across platforms. Windows keeps its own rich FreeformNotesXaml for in-app formatting;
    // Mac has no rich text editor, so it reads/writes this field directly.
    public string? NotesPlainText { get; set; }

    public double Completion
    {
        get
        {
            double total = 0;
            foreach (var goal in Goals)
                total += (goal.Weight / 100.0) * goal.Completion;
            return total;
        }
    }

    public string CompletionPercentText => $"{Math.Round(Math.Clamp(Completion, 0, 1) * 100)}%";

    public string NextGoalSummary => NextIncompleteGoal() is Goal g
        ? $"Next: {g.Name}"
        : (Goals.Count == 0 ? "No goals yet" : "All goals complete");

    // What the newest session note said to do next (the "Next:" from the finish prompt), with the
    // note's time. Null when the newest note has none: an older one would be stale by now.
    public (string Text, DateTime When)? NoteNext()
    {
        var latest = Notes.OrderByDescending(n => n.Timestamp).FirstOrDefault();
        if (latest == null) return null;

        const string marker = "Next: ";
        var at = latest.Text.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;

        var rest = latest.Text[(at + marker.Length)..];
        var end = rest.IndexOf("  |  ", StringComparison.Ordinal);
        var text = (end >= 0 ? rest[..end] : rest).Trim();
        return text.Length > 0 ? (text, latest.Timestamp) : null;
    }

    public Goal? NextIncompleteGoal()
    {
        foreach (var goal in Goals)
        {
            var found = FindNextIncomplete(goal);
            if (found != null) return found;
        }
        return null;
    }

    private static Goal? FindNextIncomplete(Goal goal)
    {
        if (goal.SubGoals.Count == 0)
            return goal.IsComplete ? null : goal;

        foreach (var sub in goal.SubGoals)
        {
            var found = FindNextIncomplete(sub);
            if (found != null) return found;
        }
        return null;
    }

    public Project Clone()
    {
        var clone = new Project
        {
            Id = Id,
            Name = Name,
            LinkedModeId = LinkedModeId,
            ChannelId = ChannelId,
            IsMain = IsMain,
            ChannelName = ChannelName,
            ChannelColor = ChannelColor,
            DefaultTargetMinutes = DefaultTargetMinutes,
            DueDate = DueDate,
            FreeformNotesXaml = FreeformNotesXaml,
            NotesPlainText = NotesPlainText,
            Notes = new List<ProjectNote>(Notes)
        };
        foreach (var goal in Goals)
            clone.Goals.Add(goal.Clone());
        return clone;
    }
}
