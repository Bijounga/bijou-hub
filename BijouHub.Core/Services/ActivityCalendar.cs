using BijouHub.Models;

namespace BijouHub.Services;

public enum ActivityKind { Due, Done, Session, Event }

// One dated thing for the Calendar and the Timeline: a task that's due, a task that was finished,
// or a work session. `At` is the time of day when there is one.
public record ActivityEntry(
    DateTime Day, DateTime? At, ActivityKind Kind, string Title, string? Detail, string? Color, bool Overdue = false, int Seconds = 0, string? SourceId = null)
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
        IEnumerable<CalendarEvent> events,
        Func<string?, string?> projectColor,
        DateTime from,
        DateTime to,
        DateTime now)
    {
        var entries = new List<ActivityEntry>();

        foreach (var goal in DistinctGoals(liveGoals, plans))
        {
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

        foreach (var e in events)
        {
            if (e.Start.Date < from.Date || e.Start.Date > to.Date) continue;
            entries.Add(new ActivityEntry(e.Start.Date, e.Start, ActivityKind.Event, e.Title.Length == 0 ? "(No title)" : e.Title, null, e.Color, false, e.Minutes * 60, e.Id));
        }

        return entries.OrderBy(e => e.Day).ThenBy(e => e.SortTime).ToList();
    }

    // Every goal once: today's on-screen ones first, then the stored plans' newest first, skipping
    // a goal already seen by id or by Google task.
    public static IEnumerable<DailyGoal> DistinctGoals(IEnumerable<DailyGoal> liveGoals, IEnumerable<DailyPlan> plans)
    {
        var seenIds = new HashSet<string>();
        var seenTasks = new HashSet<string>();
        foreach (var goal in liveGoals.Concat(plans.OrderByDescending(p => p.Date, StringComparer.Ordinal).SelectMany(p => p.Goals)))
            if (seenIds.Add(goal.Id) && (goal.TaskId == null || seenTasks.Add(goal.TaskId)))
                yield return goal;
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

// The Timeline's rows: tasks as bars from when they were added to when they're due (or done),
// events as one-day bars, and work as a bar for each day worked, per project.
public enum TimelineKind { Task, Event, Work }

public record TimelineBar(DateTime Start, DateTime End, string Label, string? Detail, string? Color, TimelineKind Kind, bool Done = false, bool Overdue = false, string? SourceId = null);

public record TimelineRow(List<TimelineBar> Bars);

public record TimelineSection(string Title, List<TimelineRow> Rows);

public static class TimelineModel
{
    public static List<TimelineSection> Build(
        IEnumerable<DailyGoal> liveGoals,
        IEnumerable<DailyPlan> plans,
        IEnumerable<SessionRecord> sessions,
        IEnumerable<CalendarEvent> events,
        Func<string?, string?> projectColor,
        DateTime from,
        DateTime to,
        DateTime now)
    {
        var sections = new List<TimelineSection>();

        // Tasks: a bar from the day it was added to the day it's due (open) or was finished (done).
        var tasks = new List<TimelineRow>();
        foreach (var goal in ActivityCalendar.DistinctGoals(liveGoals, plans))
        {
            DateTime? end = goal.Done ? goal.CompletedAt?.Date : DueText.Day(goal.Due);
            if (end == null) continue;
            var start = goal.CreatedAt.Date <= end.Value ? goal.CreatedAt.Date : end.Value;
            if (end.Value < from.Date || start > to.Date) continue;
            var overdue = !goal.Done && DueText.Chip(goal, end.Value, now).Overdue;
            tasks.Add(new TimelineRow(new List<TimelineBar> { new(start, end.Value, goal.Text, goal.ProjectName, projectColor(goal.ProjectId), TimelineKind.Task, goal.Done, overdue) }));
        }
        if (tasks.Count > 0)
            sections.Add(new TimelineSection("Tasks", tasks.OrderBy(r => r.Bars[0].Start).ThenBy(r => r.Bars[0].End).ToList()));

        // Events: one bar on the day.
        var eventRows = events
            .Where(e => e.Start.Date >= from.Date && e.Start.Date <= to.Date)
            .OrderBy(e => e.Start)
            .Select(e => new TimelineRow(new List<TimelineBar>
            {
                new(e.Start.Date, e.Start.Date, e.Title.Length == 0 ? "(No title)" : e.Title, $"{e.Start:h:mm tt} – {e.End:h:mm tt}".Replace("  ", " "), e.Color, TimelineKind.Event, SourceId: e.Id)
            }))
            .ToList();
        if (eventRows.Count > 0) sections.Add(new TimelineSection("Events", eventRows));

        // Work: a row per project (or mode, for time without one) with a bar on each day worked.
        var work = sessions
            .Where(s => s.StartTime.Date >= from.Date && s.StartTime.Date <= to.Date)
            .GroupBy(s => s.ProjectName ?? s.ModeName)
            .Select(g => new TimelineRow(g.GroupBy(s => s.StartTime.Date)
                .OrderBy(d => d.Key)
                .Select(d => new TimelineBar(d.Key, d.Key, g.Key, ActivityCalendar.Duration(d.Sum(s => s.ActiveSeconds)), projectColor(g.First().ProjectId), TimelineKind.Work))
                .ToList()))
            .OrderBy(r => r.Bars[0].Start)
            .ToList();
        if (work.Count > 0) sections.Add(new TimelineSection("Work", work));

        return sections;
    }
}
