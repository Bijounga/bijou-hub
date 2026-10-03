using System.Globalization;
using System.Text.RegularExpressions;
using BijouHub.Models;

namespace BijouHub.Services;

// Labels and parsing for goal dates and times.
public static partial class DueText
{
    public static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateTime? Day(string? key) =>
        key != null && DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    // The chip on a goal: the date (left out when it's the day being looked at) and the time.
    // "Tomorrow 3:00 PM", "Fri", "Oct 9", "Yesterday"; null when there's nothing to show.
    public static (string? Text, bool Overdue) Chip(DailyGoal goal, DateTime viewedDay, DateTime now)
    {
        if (goal.Due == null && goal.DueTime == null) return (null, false);
        var today = now.Date;
        var day = Day(goal.Due) ?? today;
        var time = Time(goal.DueTime);

        var parts = new List<string>();
        if (day != viewedDay) parts.Add(DayLabel(day, today));
        if (time != null) parts.Add(Time12(time.Value));

        var overdue = !goal.Done && (day < today || (time != null && day == today && day.Add(time.Value.ToTimeSpan()) <= now));
        return (parts.Count == 0 ? null : string.Join(" ", parts), overdue);
    }

    public static string DayLabel(DateTime day, DateTime today)
    {
        var days = (day - today).Days;
        return days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            > 1 and < 7 => day.ToString("ddd", CultureInfo.CurrentCulture),
            _ => day.Year == today.Year ? day.ToString("MMM d", CultureInfo.CurrentCulture) : day.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)
        };
    }

    public static TimeOnly? Time(string? value) =>
        value != null && TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var time) ? time : null;

    public static string Time12(TimeOnly time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);

    // What people type for a time: "3pm", "3:30 pm", "15:30", "930", "noon".
    public static TimeOnly? ParseTime(string? text)
    {
        var value = (text ?? "").Trim().ToLowerInvariant().Replace(".", "");
        if (value.Length == 0) return null;
        if (value == "noon") return new TimeOnly(12, 0);
        if (value == "midnight") return new TimeOnly(0, 0);

        var m = TimePattern().Match(value);
        if (!m.Success) return null;
        var digits = m.Groups[1].Value + m.Groups[2].Value;
        int hour, minute;
        if (m.Groups[2].Success && m.Groups[2].Value.Length > 0) { hour = int.Parse(m.Groups[1].Value); minute = int.Parse(m.Groups[2].Value); }
        else if (digits.Length >= 3) { hour = int.Parse(digits[..^2]); minute = int.Parse(digits[^2..]); }
        else { hour = int.Parse(digits); minute = 0; }

        var meridiem = m.Groups[3].Value;
        if (meridiem.StartsWith('p') && hour < 12) hour += 12;
        if (meridiem.StartsWith('a') && hour == 12) hour = 0;
        return hour is >= 0 and < 24 && minute is >= 0 and < 60 ? new TimeOnly(hour, minute) : null;
    }

    [GeneratedRegex(@"^(\d{1,4})(?::(\d{2}))?\s*(am|pm|a|p)?$")]
    private static partial Regex TimePattern();
}
