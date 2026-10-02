using System.IO;
using System.Text.Json;
using BijouHub.Models;

namespace BijouHub.Services;

// Daily goals/notes, one entry per day, in the sync folder alongside projects and session
// history so a day's plan follows the user between machines.
public class DailyPlanStore
{
    private const int KeepDays = 120;
    private readonly string _filePath = Path.Combine(DataPaths.SyncDir, "daily.json");

    public static string Key(DateTime day) => day.ToString("yyyy-MM-dd");

    public List<DailyPlan> LoadAll()
    {
        if (!File.Exists(_filePath)) return new List<DailyPlan>();
        try
        {
            return JsonSerializer.Deserialize<List<DailyPlan>>(File.ReadAllText(_filePath)) ?? new List<DailyPlan>();
        }
        catch (JsonException)
        {
            return new List<DailyPlan>();
        }
    }

    public DailyPlan LoadDay(DateTime day)
    {
        var key = Key(day);
        return LoadAll().FirstOrDefault(p => p.Date == key) ?? new DailyPlan { Date = key };
    }

    // Unfinished goals from the most recent earlier day that had any — what "carry over" offers.
    public (string Date, List<DailyGoal> Goals)? LatestUnfinishedBefore(DateTime day)
    {
        var key = Key(day);
        var previous = LoadAll()
            .Where(p => string.CompareOrdinal(p.Date, key) < 0 && p.Goals.Count > 0)
            .OrderByDescending(p => p.Date)
            .FirstOrDefault();
        if (previous == null) return null;

        var open = previous.Goals.Where(g => !g.Done).ToList();
        return open.Count == 0 ? null : (previous.Date, open);
    }

    public void SaveDay(DailyPlan plan)
    {
        var all = LoadAll();
        all.RemoveAll(p => p.Date == plan.Date);
        all.Add(plan);

        // Old days are only useful for carry-over and a glance back; don't let the file grow forever.
        var cutoff = Key(DateTime.Today.AddDays(-KeepDays));
        all = all.Where(p => string.CompareOrdinal(p.Date, cutoff) >= 0).OrderBy(p => p.Date).ToList();

        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }
}
