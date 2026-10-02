using System.Globalization;
using System.IO;
using System.Text.Json;
using BijouHub.Models;
using Microsoft.Data.Sqlite;

namespace BijouHub.Services;

// The home page's project board (formerly the separate BijouBoard app): one card per BijouHub
// project, joined by exact project name (case/space-insensitive) to its BijouDocs script and
// BijouMusic project. No fuzzy matching on purpose — a near-miss name shows as "not linked"
// rather than a wrong guess. Everything outside BijouHub is opened read-only.
public static class ProjectBoardService
{
    public sealed record Card(
        Project Project,
        string Title,
        string Subtitle,
        string StatusLabel,
        string StatusTone,
        string ScriptLabel,
        string ScriptTone,
        string ScriptDetail,
        string MusicLabel,
        string MusicTone,
        string MusicDetail,
        string TimeThisWeek,
        string GoalsText,
        double GoalsFraction,
        string NextGoal)
    {
        public string StatusPillText => StatusLabel.ToUpperInvariant();
    }

    public sealed record Board(IReadOnlyList<Card> Cards, int ReadyToPublish, int BehindPace, int NeedMusic, int Active);

    private sealed record Script(string Title, int Done, int Total, DateTime? Due, DateTime? Updated);

    private sealed record MusicProject(string Name, int Tracks, DateTime? Updated);

    public static Board Build(IReadOnlyList<Project> projects, IReadOnlyList<SessionRecord> sessions)
    {
        var scripts = ReadBijouDocs(out var docsFound);
        var music = ReadBijouMusic(out var musicFound);

        var scriptByTitle = scripts
            .GroupBy(s => Normalize(s.Title))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Updated).First());
        var musicByName = music
            .GroupBy(m => Normalize(m.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var weekStart = StartOfWeek(DateTime.Now);
        var cards = new List<Card>();

        foreach (var project in projects)
        {
            scriptByTitle.TryGetValue(Normalize(project.Name), out var script);
            musicByName.TryGetValue(Normalize(project.Name), out var track);

            var projectSessions = sessions.Where(s => s.ProjectId == project.Id).ToList();
            var secondsThisWeek = projectSessions.Where(s => s.StartTime >= weekStart).Sum(s => s.ActiveSeconds);
            var lastWorked = projectSessions.Count > 0 ? projectSessions.Max(s => s.StartTime) : (DateTime?)null;

            var leaves = Leaves(project.Goals).ToList();
            var goalsDone = leaves.Count(g => g.IsComplete);

            var scriptReady = script is { Total: > 0 } && script.Done == script.Total;
            var hasMusic = track is { Tracks: > 0 };
            var allGoalsDone = leaves.Count == 0 || goalsDone == leaves.Count;
            var nothingStarted = (script == null || script.Total == 0) && !hasMusic && projectSessions.Count == 0 && goalsDone == 0;

            var (statusLabel, statusTone) =
                nothingStarted ? ("Not started", "neutral")
                : scriptReady && hasMusic && allGoalsDone ? ("Ready to publish", "success")
                : script?.Due is DateTime due && (due - DateTime.Today).TotalDays < 2 && !scriptReady ? ("Behind pace", "critical")
                : ("In progress", "accent");

            var (scriptLabel, scriptTone, scriptDetail) =
                !docsFound ? ("—", "neutral", "BijouDocs not found")
                : script == null ? ("Not started", "neutral", "No script with this name")
                : script.Total == 0 ? ("Not started", "neutral", "No sections yet")
                : scriptReady ? ("Ready", "success", $"{script.Done}/{script.Total} sections scripted")
                : ("Drafting", "warning", $"{script.Done}/{script.Total} sections scripted");

            var (musicLabel, musicTone, musicDetail) =
                !musicFound ? ("—", "neutral", "BijouMusic not found")
                : hasMusic ? ($"{track!.Tracks} track{(track.Tracks == 1 ? "" : "s")}", "success", "Attached in BijouMusic")
                : ("None yet", "warning", "No tracks attached");

            var goalsText = leaves.Count == 0
                ? "No goals set"
                : $"Goals — {goalsDone}/{leaves.Count} ({Math.Round(Math.Clamp(project.Completion, 0, 1) * 100)}%)";

            cards.Add(new Card(
                project,
                project.Name,
                Subtitle(lastWorked, script),
                statusLabel,
                statusTone,
                scriptLabel,
                scriptTone,
                scriptDetail,
                musicLabel,
                musicTone,
                musicDetail,
                FormatHours(secondsThisWeek),
                goalsText,
                Math.Clamp(project.Completion, 0, 1),
                project.NextIncompleteGoal() is Goal next ? $"Next: {next.Name}" : ""));
        }

        return new Board(
            cards,
            cards.Count(c => c.StatusLabel == "Ready to publish"),
            cards.Count(c => c.StatusLabel == "Behind pace"),
            cards.Count(c => c.MusicTone == "warning" && c.StatusLabel != "Not started"),
            cards.Count);
    }

    private static string Subtitle(DateTime? lastWorked, Script? script)
    {
        var parts = new List<string>();
        parts.Add(lastWorked is DateTime worked ? $"Worked on {Relative(worked)}" : "No time logged yet");

        if (script?.Due is DateTime due)
        {
            var days = (due.Date - DateTime.Today).Days;
            parts.Add(days switch
            {
                < 0 => $"Overdue since {due:MMM d}",
                0 => "Due today",
                1 => "Due tomorrow",
                _ => $"Due {due:MMM d}"
            });
        }
        return string.Join("  ·  ", parts);
    }

    private static string Relative(DateTime when)
    {
        var days = (DateTime.Today - when.Date).Days;
        return days switch
        {
            0 => "today",
            1 => "yesterday",
            < 7 => when.ToString("dddd", CultureInfo.CurrentCulture),
            _ => when.ToString("MMM d", CultureInfo.CurrentCulture)
        };
    }

    private static string FormatHours(int seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalHours >= 1) return span.Minutes == 0 ? $"{(int)span.TotalHours}h" : $"{(int)span.TotalHours}h {span.Minutes}m";
        return seconds == 0 ? "0h" : $"{Math.Max(1, span.Minutes)}m";
    }

    private static DateTime StartOfWeek(DateTime now)
    {
        var diff = ((int)now.DayOfWeek + 6) % 7; // Monday = 0
        return now.Date.AddDays(-diff);
    }

    private static IEnumerable<Goal> Leaves(IEnumerable<Goal> goals)
    {
        foreach (var goal in goals)
        {
            if (goal.SubGoals.Count == 0) yield return goal;
            else foreach (var leaf in Leaves(goal.SubGoals)) yield return leaf;
        }
    }

    private static string Normalize(string? name) =>
        string.Join(' ', (name ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ---------- BijouDocs: one JSON file per script, in its (possibly synced) storage folder ----------

    private static string BijouDocsDir()
    {
        try
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bijoudocs", "settings.json");
            if (File.Exists(settingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (doc.RootElement.TryGetProperty("storageDir", out var dir) && dir.GetString() is { Length: > 0 } configured)
                    return configured;
            }
        }
        catch
        {
            // Unreadable settings — fall back to BijouDocs' default folder.
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BijouDocs");
    }

    private static List<Script> ReadBijouDocs(out bool found)
    {
        var result = new List<Script>();
        var dir = BijouDocsDir();
        found = Directory.Exists(dir);
        if (!found) return result;

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var name = Path.GetFileName(file);
            // BijouDocs keeps snapshot and sync-conflict copies beside each script; only the live file counts.
            if (name.Contains(".snapshot-") || name.Contains(".conflict-")) continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("sections", out var sections) ||
                    sections.ValueKind != JsonValueKind.Array) continue;

                var total = sections.GetArrayLength();
                var done = sections.EnumerateArray().Count(s =>
                    s.ValueKind == JsonValueKind.Object && s.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True);

                DateTime? due = root.TryGetProperty("dueDate", out var dueEl) && dueEl.ValueKind == JsonValueKind.String &&
                                DateTime.TryParse(dueEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDue)
                    ? parsedDue
                    : null;
                DateTime? updated = root.TryGetProperty("updatedAt", out var updatedEl) && updatedEl.TryGetInt64(out var ms)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime
                    : null;
                var title = root.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                    ? titleEl.GetString() ?? ""
                    : "";

                result.Add(new Script(title, done, total, due, updated));
            }
            catch
            {
                // Not a script, or mid-write — skip it this time.
            }
        }
        return result;
    }

    // ---------- BijouMusic: its SQLite library, opened read-only ----------

    private static List<MusicProject> ReadBijouMusic(out bool found)
    {
        var result = new List<MusicProject>();
        var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "music-browser", "library.sqlite3");
        found = File.Exists(dbPath);
        if (!found) return result;

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 2
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT p.name, p.updated_at, (SELECT COUNT(*) FROM project_tracks pt WHERE pt.project_id = p.id)
                  FROM projects p WHERE p.archived = 0";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                DateTime? updated = reader.IsDBNull(1) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)).LocalDateTime;
                result.Add(new MusicProject(reader.GetString(0), reader.GetInt32(2), updated));
            }
        }
        catch
        {
            // Locked mid-write or an older schema — show music as unavailable rather than fail the board.
            found = false;
            result.Clear();
        }
        return result;
    }
}
