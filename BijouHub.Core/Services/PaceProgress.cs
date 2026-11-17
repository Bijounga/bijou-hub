using BijouHub.Models;

namespace BijouHub.Services;

// A pace with where it stands, ready to draw.
public sealed record PaceItem(TaskPace Pace, PaceProgress Progress);

// How far along a task's pace is: time put into the task so far today (or this week) against the
// amount asked for. Only sessions with the task picked count toward it.
public sealed record PaceProgress(int DoneSeconds, int TargetSeconds, bool Active, string Period)
{
    public double Fraction => TargetSeconds <= 0 ? 0 : Math.Min(1, (double)DoneSeconds / TargetSeconds);
    public bool Done => DoneSeconds >= TargetSeconds;
    public int RemainingSeconds => Math.Max(0, TargetSeconds - DoneSeconds);

    // Whole minutes still to do, rounded up (a minute and a half left is 2).
    public int RemainingMinutes => (RemainingSeconds + 59) / 60;

    // The short form for a row: "35m left", "done", or "rest day" for a weekdays pace at the weekend.
    public string Short => !Active ? "rest day" : Done ? "done" : $"{DurationText.Format(RemainingMinutes)} left";

    // The long form, for a tooltip: "25m of 1h today".
    public string Summary => $"{DurationText.Format(DoneSeconds / 60)} of {DurationText.Format(TargetSeconds / 60)} {Period}";

    // `liveSeconds` is the running session's time, when it's on this task (it isn't in the log yet).
    public static PaceProgress For(TaskPace pace, IEnumerable<SessionRecord> sessions, int liveSeconds, DateTime now)
    {
        var weekly = pace.Rhythm == PaceRhythm.Weekly;
        var since = weekly ? now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7)) : now.Date; // weeks start on Monday
        var seconds = sessions.Where(s => s.TaskKey == pace.Key && s.StartTime >= since).Sum(s => s.ActiveSeconds) + Math.Max(0, liveSeconds);
        var active = pace.Rhythm != PaceRhythm.Weekdays || now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        return new PaceProgress(seconds, pace.Minutes * 60, active, weekly ? "this week" : "today");
    }
}
