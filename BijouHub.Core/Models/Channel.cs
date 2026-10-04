namespace BijouHub.Models;

// A YouTube channel (or any bucket) projects belong to: a name and a color that follows the
// project through the sidebar, the board and its tasks.
public class Channel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New channel";

    // #RRGGBB.
    public string Color { get; set; } = ChannelPalette.Colors[0];
}

public static class ChannelPalette
{
    // Distinct on both dark and light themes, and from each other.
    public static readonly string[] Colors =
    {
        "#33B5FF", // blue
        "#A78BFA", // violet
        "#F59E0B", // amber
        "#34D399", // green
        "#F472B6", // pink
        "#F87171", // red
        "#2DD4BF", // teal
        "#94A3B8"  // slate
    };

    // The first color no channel has taken yet (wrapping around once they're all used).
    public static string Next(IEnumerable<Channel> existing)
    {
        var used = existing.Select(c => c.Color.ToUpperInvariant()).ToHashSet();
        return Colors.FirstOrDefault(c => !used.Contains(c.ToUpperInvariant())) ?? Colors[existing.Count() % Colors.Length];
    }
}

// A section title in the sidebar's project list: a channel with how many projects it holds.
// Not stored; rebuilt whenever projects or channels change.
public sealed record ChannelHeader(string? ChannelId, string Name, string Color, int Count, bool Collapsed)
{
    public bool IsNoChannel => ChannelId == null;

    // What screen readers announce for the section's row.
    public override string ToString() => $"{Name}, {Count} project{(Count == 1 ? "" : "s")}";
}
