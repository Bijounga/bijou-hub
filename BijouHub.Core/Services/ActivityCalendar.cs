using BijouHub.Models;

namespace BijouHub.Services;

public enum ActivityKind { Due, Done, Session }

// One dated thing for the Calendar and the Timeline: a task that's due, a task that was finished,
// or a work session. `At` is the time of day when there is one.
public record ActivityEntry(
    DateTime Day, DateTime? At, ActivityKind Kind, string Title, string? Detail, string? Color, bool Overdue = false, int Seconds = 0)
{
    // Where the entry sits within its day: timed ones in order, untimed ones after.
    public TimeSpan SortTime => At?.TimeOfDay ?? new TimeSpan(23, 59, 59);
}

// Gathers goals (open with a due date, finished) and sessions into dated entries, and lays out
// month grids. Pure functions of what's passed in, so Windows and Mac draw from the same facts.
public static class ActivityCalendar
{
    // `liveGoals` are today's on-screen goals; the stored plans fill in the other days. A goal
    // that appears in both (same id, or the same Google task) counts once, the live copy winning.
    public static List<ActivityEntry> Build(
        IEnumerable<DailyGoal> liveGoals,
        IEnumerable<DailyPlan> plans,
        IEnumerable<SessionRecord> sessions,
        Func<string?, string?> projectColor,
        DateTime from,
        DateTime to,
        DateTime now)
    {
        var entries = new List<ActivityEntry>();
        var seenIds = new HashSet<string>();
        var seenTasks = new HashSet<string>();

        bool Fresh(DailyGoal goal) =>
            seenIds.Add(goal.Id) && (goal.TaskId == null || seenTasks.Add(goal.TaskId));

        foreach (var goal in liveGoals.Concat(plans.OrderByDescending(p => p.Date, StringComparer.Ordinal).SelectMany(p => p.Goals)))
        {
            if (!Fresh(goal)) continue;
            var color = projectColor(goal.ProjectId);
            var detail = goal.ProjectName;

            if (goal.Done)
            {
                if (goal.CompletedAt is { } finished && finished.Date >= from.Date && finished.Date <= to.Date)
                    entries.Add(new ActivityEntry(finished.Date, finished, ActivityKind.Done, goal.Text, detail, color));
            }
            else if (DueText.Day(goal.Due) is { } due && due >= from.Date && due <= to.Date)
            {
                var overdue = DueText.Chip(goal, due, now).Overdue;
                entries.Add(new ActivityEntry(due, goal.DueAt, ActivityKind.Due, goal.Text, detail, color, overdue));
            }
        }

        foreach (var session in sessions)
        {
            if (session.StartTime.Date < from.Date || session.StartTime.Date > to.Date) continue;
            var title = session.ProjectName ?? session.ModeName;
            var detail = session.ProjectName == null ? null : session.ModeName;
            entries.Add(new ActivityEntry(session.StartTime.Date, session.StartTime, ActivityKind.Session, title, detail, projectColor(session.ProjectId), false, session.ActiveSeconds));
        }

        return entries.OrderBy(e => e.Day).ThenBy(e => e.SortTime).ToList();
    }

    // The 6×7 grid of days a month view shows, starting on `firstDayOfWeek`.
    public static List<DateTime> MonthGrid(DateTime month, DayOfWeek firstDayOfWeek)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var offset = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var start = first.AddDays(-offset);
        return Enumerable.Range(0, 42).Select(i => start.AddDays(i)).ToList();
    }

    // "2h 10m", "45m", "30s".
    public static string Duration(int seconds) =>
        seconds >= 3600 ? $"{seconds / 3600}h {seconds % 3600 / 60:D2}m" : seconds >= 60 ? $"{seconds / 60}m" : $"{seconds}s";
}
