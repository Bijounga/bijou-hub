using System.Collections.ObjectModel;

namespace BijouHub.Models;

public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Project";
    public string? LinkedModeId { get; set; }
    public ObservableCollection<Goal> Goals { get; set; } = new();
    public List<ProjectNote> Notes { get; set; } = new();
    public int? DefaultTargetMinutes { get; set; }
    public string? FreeformNotesXaml { get; set; }

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
            DefaultTargetMinutes = DefaultTargetMinutes,
            FreeformNotesXaml = FreeformNotesXaml,
            Notes = new List<ProjectNote>(Notes)
        };
        foreach (var goal in Goals)
            clone.Goals.Add(goal.Clone());
        return clone;
    }
}
