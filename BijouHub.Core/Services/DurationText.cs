using System.Globalization;
using System.Text.RegularExpressions;

namespace BijouHub.Services;

// Reads timer lengths the way people type them — "45", "45m", "1:30", "1h", "1h 30m", "1.5h",
// "90 min" — into minutes. Same rules as the Stream Deck plugin's parseDuration
// (StreamDeck/src/duration.ts); keep the two in sync.
public static partial class DurationText
{
    private const int MaxMinutes = 24 * 60;

    public static int? TryParseMinutes(string? text)
    {
        var value = (text ?? "").Trim().ToLowerInvariant();
        if (value.Length == 0) return null;

        var clock = ClockPattern().Match(value);
        if (clock.Success)
            return Positive(int.Parse(clock.Groups[1].Value) * 60 + int.Parse(clock.Groups[2].Value));

        if (double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var plain))
            return Positive((int)Math.Round(plain));

        var parts = PartsPattern().Match(value);
        if (parts.Success && (parts.Groups[1].Success || parts.Groups[2].Success))
        {
            var hours = parts.Groups[1].Success ? double.Parse(parts.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var minutes = parts.Groups[2].Success ? int.Parse(parts.Groups[2].Value) : 0;
            return Positive((int)Math.Round(hours * 60 + minutes));
        }

        return null;
    }

    // "25/5", "50 / 10", "1h/15" → a focus/break cycle. Null for anything else.
    public static PomodoroPlan? TryParsePomodoro(string? text)
    {
        var parts = (text ?? "").Split('/');
        if (parts.Length != 2) return null;
        return TryParseMinutes(parts[0]) is int focus && TryParseMinutes(parts[1]) is int rest && rest <= 120
            ? new PomodoroPlan(focus, rest)
            : null;
    }

    // 45 → "45m", 60 → "1h", 90 → "1h 30m".
    public static string Format(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        if (h == 0) return $"{m}m";
        return m == 0 ? $"{h}h" : $"{h}h {m}m";
    }

    private static int? Positive(int minutes) => minutes is > 0 and <= MaxMinutes ? minutes : null;

    [GeneratedRegex(@"^(\d{1,2}):(\d{1,2})$")]
    private static partial Regex ClockPattern();

    [GeneratedRegex(@"^(?:(\d+(?:\.\d+)?)\s*h(?:ours?|rs?)?)?\s*(?:(\d+)\s*(?:m|mins?|minutes?)?)?$")]
    private static partial Regex PartsPattern();
}
