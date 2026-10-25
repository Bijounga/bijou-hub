using System.Globalization;
using System.IO;
using System.Text.Json;
using BijouHub.Models;

namespace BijouHub.Services;

// The home page's project board (formerly the separate BijouBoard app): one card per BijouHub
// project, joined by exact project name (case/space-insensitive) to its BijouDocs script. No fuzzy
// matching on purpose — a near-miss name shows as "not linked" rather than a wrong guess.
// Everything outside BijouHub is opened read-only.
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
        string TimeThisWeek,
        string GoalsText,
        double GoalsFraction,
        string NextGoal,
        string DueLabel,
        string DueRelative,
        string DueTone)
    {
        public string StatusPillText => StatusLabel.ToUpperInvariant();
    }

    public sealed record Board(IReadOnlyList<Card> Cards, int ReadyToPublish, int BehindPace, int Active)
    {
        // The same board for just some projects (a channel), with its counts recomputed.
        public Board Filtered(Func<Card, bool> keep) => Summarize(Cards.Where(keep).ToList());
    }

    public static Board Summarize(IReadOnlyList<Card> cards) => new(
        cards,
        cards.Count(c => c.StatusLabel == "Ready to publish"),
        cards.Count(c => c.StatusLabel == "Behind pace"),
        cards.Count);

    private sealed record Script(string Title, int Done, int Total, DateTime? Due, DateTime? Updated);

    public static Board Build(IReadOnlyList<Project> projects, IReadOnlyList<SessionRecord> sessions)
    {
        var scripts = ReadBijouDocs(out var docsFound);

        var scriptByTitle = scripts
            .GroupBy(s => Normalize(s.Title))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Updated).First());

        var weekStart = StartOfWeek(DateTime.Now);
        var cards = new List<Card>();

        foreach (var project in projects)
        {
            scriptByTitle.TryGetValue(Normalize(project.Name), out var script);

            var projectSessions = sessions.Where(s => s.ProjectId == project.Id).ToList();
            var secondsThisWeek = projectSessions.Where(s => s.StartTime >= weekStart).Sum(s => s.ActiveSeconds);
            var lastWorked = projectSessions.Count > 0 ? projectSessions.Max(s => s.StartTime) : (DateTime?)null;

            var leaves = Leaves(project.Goals).ToList();
            var goalsDone = leaves.Count(g => g.IsComplete);

            var scriptReady = script is { Total: > 0 } && script.Done == script.Total;
            var allGoalsDone = leaves.Count == 0 || goalsDone == leaves.Count;
            var nothingStarted = (script == null || script.Total == 0) && projectSessions.Count == 0 && goalsDone == 0;

            // The project's own date wins; a script's date in BijouDocs is the fallback.
            var dueDay = DueText.Day(project.DueDate) ?? script?.Due?.Date;

            var (statusLabel, statusTone) =
                nothingStarted ? ("Not started", "neutral")
                : scriptReady && allGoalsDone ? ("Ready to publish", "success")
                : dueDay is DateTime due && (due - DateTime.Today).TotalDays < 2 && !scriptReady ? ("Behind pace", "critical")
                : ("In progress", "accent");

            var (scriptLabel, scriptTone, scriptDetail) =
                !docsFound ? ("—", "neutral", "BijouDocs not found")
                : script == null ? ("Not started", "neutral", "No script with this name")
                : script.Total == 0 ? ("Not started", "neutral", "No sections yet")
                : scriptReady ? ("Ready", "success", $"{script.Done}/{script.Total} sections scripted")
                : ("Drafting", "warning", $"{script.Done}/{script.Total} sections scripted");

            var goalsText = leaves.Count == 0
                ? "No goals set"
                : $"Goals — {goalsDone}/{leaves.Count} ({Math.Round(Math.Clamp(project.Completion, 0, 1) * 100)}%)";

            var (dueLabel, dueRelative, dueTone) = DueInfo(dueDay, statusLabel == "Ready to publish");

            cards.Add(new Card(
                project,
                project.Name,
                Subtitle(lastWorked),
                statusLabel,
                statusTone,
                scriptLabel,
                scriptTone,
                scriptDetail,
                FormatHours(secondsThisWeek),
                goalsText,
                Math.Clamp(project.Completion, 0, 1),
                project.NextIncompleteGoal() is Goal next ? $"Next: {next.Name}" : "",
                dueLabel,
                dueRelative,
                dueTone));
        }

        return Summarize(cards);
    }

    private static string Subtitle(DateTime? lastWorked) =>
        lastWorked is DateTime worked ? $"Worked on {Relative(worked)}" : "No time logged yet";

    // The due strip on a card: the date ("Due Wed, Nov 10"), how far off it is ("in 5 days",
    // "2 days overdue") and a tone for it: red when overdue or within a day, amber within a
    // week, quiet grey beyond that, green once the project is ready to publish. All empty when there's no date.
    private static (string Label, string Relative, string Tone) DueInfo(DateTime? day, bool ready)
    {
        if (day is not DateTime due) return ("", "", "");
        var today = DateTime.Today;
        var days = (due.Date - today).Days;

        var format = due.Year == today.Year ? "ddd, MMM d" : "ddd, MMM d, yyyy";
        var label = $"Due {due.ToString(format, CultureInfo.CurrentCulture)}";
        var relative = days switch
        {
            < -1 => $"{-days} days overdue",
            -1 => "1 day overdue",
            0 => "today",
            1 => "tomorrow",
            < 14 => $"in {days} days",
            < 60 => $"in {days / 7} weeks",
            _ => $"in {days / 30} months"
        };
        var tone = ready && days >= 0 ? "success" : days <= 1 ? "critical" : days <= 7 ? "warning" : "neutral";
        return (label, relative, tone);
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

    // Where Electron apps (BijouDocs) keep their data: %APPDATA% on Windows,
    // ~/Library/Application Support on a Mac.
    private static string ElectronAppData() =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    // ---------- BijouDocs: one JSON file per script, in its (possibly synced) storage folder ----------

    private static string BijouDocsDir()
    {
        try
        {
            var settingsPath = Path.Combine(ElectronAppData(), "bijoudocs", "settings.json");
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
}
