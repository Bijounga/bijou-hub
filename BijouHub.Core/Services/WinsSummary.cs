using BijouHub.Models;

namespace BijouHub.Services;

// What to show after a session (and when winding down the day): the facts, plainly. No made-up praise:
// every line comes from something that actually happened, and a missed day is never counted against you.
public record WinsSummary(string Headline, List<string> Lines, int SessionMinutes, int TodayMinutes, bool TargetHit);

public static class WinsSummaryBuilder
{
    private const int ShortSessionMinutes = 10;
    private const int LongSessionMinutes = 20;
    private const int WorkedDayMinutes = 10;

    // `finished` is the session that just ended and is already in `history`.
    public static WinsSummary AfterSession(SessionRecord finished, IReadOnlyCollection<SessionRecord> history, int? targetMinutes, int tasksDoneToday, DateTime now)
    {
        var today = now.Date;
        var minutes = finished.ActiveSeconds / 60;
        var todaySeconds = history.Where(r => !r.IsStudy && r.StartTime.Date == today).Sum(r => r.ActiveSeconds);
        var before = todaySeconds - (finished.IsStudy ? 0 : finished.ActiveSeconds);
        var todayMinutes = todaySeconds / 60;
        var hit = targetMinutes is int t && todaySeconds >= t * 60;
        var justHit = hit && targetMinutes is int t2 && before < t2 * 60;

        var weekStart = today.AddDays(-6);
        var others = history.Where(r => !r.IsStudy && r.Id != finished.Id && r.StartTime.Date >= weekStart).ToList();
        var longest = !finished.IsStudy && minutes >= LongSessionMinutes
            && (others.Count == 0 || finished.ActiveSeconds >= others.Max(r => r.ActiveSeconds));

        string headline;
        if (justHit) headline = "You hit your target today.";
        else if (longest && others.Count > 0) headline = "Your longest session this week.";
        else if (minutes < ShortSessionMinutes) headline = "A short one. It still counts.";
        else if (hit) headline = "Already past your target for today.";
        else headline = $"{DurationText.Format(Math.Max(minutes, 1))} of focus, done.";

        var lines = new List<string>();
        if (finished.IsStudy)
            lines.Add("Study time is logged, but it stays out of the daily target.");
        else if (targetMinutes is int target)
            lines.Add($"Today: {DurationText.Format(todayMinutes)} of {DurationText.Format(target)}");
        else
            lines.Add($"Today: {DurationText.Format(todayMinutes)}");

        if (tasksDoneToday > 0) lines.Add(tasksDoneToday == 1 ? "1 task finished today" : $"{tasksDoneToday} tasks finished today");

        var daysWorked = history.Where(r => !r.IsStudy && r.StartTime.Date >= weekStart)
            .GroupBy(r => r.StartTime.Date).Count(g => g.Sum(r => r.ActiveSeconds) >= WorkedDayMinutes * 60);
        if (daysWorked >= 2) lines.Add($"Worked {daysWorked} of the last 7 days");

        return new WinsSummary(headline, lines, minutes, todayMinutes, hit);
    }

    // For winding down the day: what got done, with no session in front of it.
    public static WinsSummary ForDay(IReadOnlyCollection<SessionRecord> history, int? targetMinutes, int tasksDoneToday, DateTime now)
    {
        var today = now.Date;
        var seconds = history.Where(r => !r.IsStudy && r.StartTime.Date == today).Sum(r => r.ActiveSeconds);
        var minutes = seconds / 60;
        var hit = targetMinutes is int t && seconds >= t * 60;

        var lines = new List<string>();
        lines.Add(targetMinutes is int target
            ? $"Worked {DurationText.Format(minutes)} of {DurationText.Format(target)}"
            : $"Worked {DurationText.Format(minutes)}");
        if (tasksDoneToday > 0) lines.Add(tasksDoneToday == 1 ? "1 task finished" : $"{tasksDoneToday} tasks finished");

        var headline = hit ? "You hit your target today."
            : minutes > 0 || tasksDoneToday > 0 ? "That's today."
            : "Nothing logged today, and that's okay. Tomorrow is a fresh start.";
        return new WinsSummary(headline, lines, 0, minutes, hit);
    }
}
