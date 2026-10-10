using System.IO;
using System.Text.Json;
using BijouHub.Models;

namespace BijouHub.Services;

// The channel list, in the sync folder next to the projects that point at it (so a channel's
// name and color follow the user between machines).
public class ChannelStore
{
    private readonly string _filePath = Path.Combine(DataPaths.SyncDir, "channels.json");

    // A file in a cloud-synced folder can be briefly unreadable (still downloading, locked by the
    // sync client), so a failed read is retried; if it still fails, the reason goes to errors.log
    // instead of silently showing no channels.
    public List<Channel> Load()
    {
        if (!File.Exists(_filePath)) return new List<Channel>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return JsonSerializer.Deserialize<List<Channel>>(File.ReadAllText(_filePath)) ?? new List<Channel>();
            }
            catch (Exception ex) when (attempt < 3 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                ErrorLog.Write($"Couldn't read channels from {_filePath}: {ex.GetType().Name}: {ex.Message}");
                return new List<Channel>();
            }
        }
    }

    // Where the channel list is looked for and what's there, for the "channels missing" notice.
    public string Describe()
    {
        try
        {
            var info = new FileInfo(_filePath);
            return info.Exists ? $"{_filePath} ({info.Length} bytes)" : $"{_filePath} (no such file)";
        }
        catch (Exception ex)
        {
            return $"{_filePath} ({ex.Message})";
        }
    }

    // What's on disk right now, for noticing a change that synced in from another computer.
    public string Fingerprint()
    {
        try
        {
            var info = new FileInfo(_filePath);
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : "";
        }
        catch
        {
            return "";
        }
    }

    public void Save(List<Channel> channels) =>
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(channels, new JsonSerializerOptions { WriteIndented = true }));
}

public static class ChannelRows
{
    // Stamps each project with its channel's name and color (UI-only fields).
    public static void Apply(IEnumerable<Project> projects, IReadOnlyList<Channel> channels)
    {
        var byId = channels.ToDictionary(c => c.Id);
        foreach (var project in projects)
        {
            var channel = project.ChannelId != null && byId.TryGetValue(project.ChannelId, out var found) ? found : null;
            project.ChannelName = channel?.Name;
            project.ChannelColor = channel?.Color;
        }
    }

    // Channels that projects point at but that aren't in the list: what happens when the channel
    // list hasn't reached this computer. Each is its id with the names of the projects in it.
    public static List<(string Id, List<string> Projects)> Orphans(IEnumerable<Project> projects, IReadOnlyList<Channel> channels)
    {
        var known = channels.Select(c => c.Id).ToHashSet();
        return projects
            .Where(p => !string.IsNullOrEmpty(p.ChannelId) && !known.Contains(p.ChannelId))
            .GroupBy(p => p.ChannelId!)
            .Select(g => (g.Key, g.Select(p => p.Name).ToList()))
            .ToList();
    }

    // Marks (or unmarks) a project as its channel's main project; marking one unmarks any other
    // in the same channel (projects with no channel share one "channel" of their own).
    public static void SetMain(IEnumerable<Project> projects, Project project, bool on)
    {
        project.IsMain = on;
        if (!on) return;
        foreach (var other in projects)
            if (other != project && other.IsMain && (other.ChannelId ?? "") == (project.ChannelId ?? ""))
                other.IsMain = false;
    }

    // The sidebar's project list: with channels, a header for each (in channel order, then "No
    // channel") followed by its projects, or just the header while collapsed. With no channels
    // at all, just the projects.
    public static List<object> Build(IReadOnlyList<Project> projects, IReadOnlyList<Channel> channels, ISet<string> collapsed)
    {
        var rows = new List<object>();
        if (channels.Count == 0)
        {
            rows.AddRange(projects.OrderByDescending(p => p.IsMain));
            return rows;
        }

        var known = channels.Select(c => c.Id).ToHashSet();
        foreach (var channel in channels)
        {
            var mine = projects.Where(p => p.ChannelId == channel.Id).OrderByDescending(p => p.IsMain).ToList();
            var isCollapsed = collapsed.Contains(channel.Id);
            rows.Add(new ChannelHeader(channel.Id, channel.Name, channel.Color, mine.Count, isCollapsed));
            if (!isCollapsed) rows.AddRange(mine);
        }

        var loose = projects.Where(p => p.ChannelId == null || !known.Contains(p.ChannelId)).OrderByDescending(p => p.IsMain).ToList();
        if (loose.Count > 0)
        {
            var isCollapsed = collapsed.Contains("");
            rows.Add(new ChannelHeader(null, "No channel", "#94A3B8", loose.Count, isCollapsed));
            if (!isCollapsed) rows.AddRange(loose);
        }
        return rows;
    }
}
