namespace BijouHub.Models;

// Something you put on the calendar by dragging out a stretch of a day: "Edited B-roll",
// "Call with sponsor". Always within one day.
public class CalendarEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    // #RRGGBB.
    public string Color { get; set; } = EventPalette.Colors[0];

    public string? Notes { get; set; }

    public int Minutes => (int)Math.Round((End - Start).TotalMinutes);

    public CalendarEvent Clone() => new()
    {
        Id = Id, Title = Title, Start = Start, End = End, Color = Color, Notes = Notes
    };

    public override string ToString() => Title;
}

public static class EventPalette
{
    // Readable behind white text on both light and dark themes.
    public static readonly string[] Colors =
    {
        "#4285F4", // blue
        "#EA4335", // red
        "#F59E0B", // amber
        "#34A853", // green
        "#8B5CF6", // violet
        "#F97316", // orange
        "#14B8A6", // teal
        "#EC4899", // pink
        "#64748B"  // slate
    };

    public static string Name(string color) => color.ToUpperInvariant() switch
    {
        "#4285F4" => "Blue",
        "#EA4335" => "Red",
        "#F59E0B" => "Amber",
        "#34A853" => "Green",
        "#8B5CF6" => "Violet",
        "#F97316" => "Orange",
        "#14B8A6" => "Teal",
        "#EC4899" => "Pink",
        "#64748B" => "Slate",
        _ => color
    };
}
