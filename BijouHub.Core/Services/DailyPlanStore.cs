using System.IO;
using System.Text.Json;
using BijouHub.Models;

namespace BijouHub.Services;

// Daily goals/notes, one entry per day, in the sync folder alongside projects and session
// history so a day's plan follows the user between machines.
//
// Saves happen on every tick, star and edit, and the sync folder is often a cloud drive's
// virtual disk, so writes run on a background thread: the in-memory copy updates at once, the
// file catches up, and bursts of changes collapse into one write. Flush() waits for the last
// write (call it on exit).
public class DailyPlanStore
{
    private const int KeepDays = 120;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _filePath = Path.Combine(DataPaths.SyncDir, "daily.json");
    private readonly object _gate = new();
    private List<DailyPlan>? _cache;
    private (DateTime Written, long Length) _cacheKey;
    private string? _pendingJson;
    private Task _writer = Task.CompletedTask;

    public static string Key(DateTime day) => day.ToString("yyyy-MM-dd");

    public List<DailyPlan> LoadAll()
    {
        lock (_gate)
        {
            // Unwritten changes are newer than the file.
            if (_cache != null && (_pendingJson != null || !_writer.IsCompleted)) return _cache;

            var info = new FileInfo(_filePath);
            if (!info.Exists) return _cache = new List<DailyPlan>();

            var key = (info.LastWriteTimeUtc, info.Length);
            if (_cache != null && key == _cacheKey) return _cache;

            try
            {
                _cache = JsonSerializer.Deserialize<List<DailyPlan>>(File.ReadAllText(_filePath)) ?? new List<DailyPlan>();
            }
            catch (JsonException)
            {
                _cache = new List<DailyPlan>();
            }
            _cacheKey = key;
            return _cache;
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
        lock (_gate)
        {
            var all = LoadAll();
            all.RemoveAll(p => p.Date == plan.Date);
            all.Add(plan);

            // Old days are only useful for carry-over and a glance back; don't let the file grow forever.
            var cutoff = Key(DateTime.Today.AddDays(-KeepDays));
            _cache = all.Where(p => string.CompareOrdinal(p.Date, cutoff) >= 0).OrderBy(p => p.Date).ToList();

            // Serialized here, on the caller's thread, so the snapshot matches what's on screen.
            _pendingJson = JsonSerializer.Serialize(_cache, WriteOptions);
            if (_writer.IsCompleted) _writer = Task.Run(WritePending);
        }
    }

    // Blocks until everything saved so far is on disk (or the attempt failed).
    public void Flush()
    {
        Task writer;
        lock (_gate) writer = _writer;
        try { writer.Wait(TimeSpan.FromSeconds(10)); }
        catch { /* reported by the next save's retry */ }

        // A write that failed leaves its JSON pending; try once more, synchronously.
        WritePending();
    }

    private void WritePending()
    {
        while (true)
        {
            string? json;
            lock (_gate)
            {
                json = _pendingJson;
                _pendingJson = null;
            }
            if (json == null) return;

            try
            {
                AtomicFile.WriteAllText(_filePath, json);
                lock (_gate)
                {
                    var info = new FileInfo(_filePath);
                    if (_pendingJson == null) _cacheKey = (info.LastWriteTimeUtc, info.Length);
                }
            }
            catch (IOException)
            {
                // Drive busy or offline: keep it for the next save or Flush, unless something newer replaced it.
                lock (_gate) _pendingJson ??= json;
                return;
            }
            catch (UnauthorizedAccessException)
            {
                lock (_gate) _pendingJson ??= json;
                return;
            }
        }
    }
}
