using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using BijouHub.Models;

namespace BijouHub.Services;

public class SessionLogService
{
    private readonly string _filePath;

    public SessionLogService()
    {
        _filePath = Path.Combine(DataPaths.SyncDir, "sessions.json");
        if (!File.Exists(_filePath))
            MigrateFromLegacySqlite();
    }

    // Session history used to live in a local SQLite file, which doesn't play well with
    // cloud-synced folders (concurrent-write corruption risk) and doesn't travel with a
    // custom sync folder anyway. Import it once into the new JSON store; the .db file is
    // left in place afterward, untouched, as a harmless backup.
    private void MigrateFromLegacySqlite()
    {
        var legacyDbPath = Path.Combine(DataPaths.LocalDir, "sessions.db");
        if (!File.Exists(legacyDbPath)) return;

        try
        {
            var records = ReadLegacySqlite(legacyDbPath);
            if (records.Count > 0)
                Save(records);
        }
        catch
        {
            // Legacy DB unreadable; start fresh rather than block the app.
        }
    }

    private static List<SessionRecord> ReadLegacySqlite(string dbPath)
    {
        var result = new List<SessionRecord>();
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, ModeName, StartTime, EndTime, ActiveSeconds, IdleSeconds,
                                    ProjectId, ProjectName, GoalId, GoalName, Note
                             FROM Sessions ORDER BY StartTime DESC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SessionRecord
            {
                Id = reader.GetInt32(0),
                ModeName = reader.GetString(1),
                StartTime = DateTime.Parse(reader.GetString(2)),
                EndTime = DateTime.Parse(reader.GetString(3)),
                ActiveSeconds = reader.GetInt32(4),
                IdleSeconds = reader.GetInt32(5),
                ProjectId = reader.IsDBNull(6) ? null : reader.GetString(6),
                ProjectName = reader.IsDBNull(7) ? null : reader.GetString(7),
                GoalId = reader.IsDBNull(8) ? null : reader.GetString(8),
                GoalName = reader.IsDBNull(9) ? null : reader.GetString(9),
                Note = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }
        return result;
    }

    // Parsed once and reused until the file changes on disk (size or write time) — Home asks for
    // several totals per visit, and the log only grows. A change synced in from another machine
    // still shows up, because its write stamp differs.
    private readonly object _gate = new();
    private List<SessionRecord>? _cache;
    private (DateTime Written, long Length) _cacheKey;

    private List<SessionRecord> LoadAll()
    {
        lock (_gate)
        {
            var info = new FileInfo(_filePath);
            if (!info.Exists)
            {
                _cache = null;
                return new List<SessionRecord>();
            }

            var key = (info.LastWriteTimeUtc, info.Length);
            if (_cache != null && key == _cacheKey) return _cache;

            var json = File.ReadAllText(_filePath);
            try
            {
                _cache = string.IsNullOrWhiteSpace(json)
                    ? new List<SessionRecord>()
                    : JsonSerializer.Deserialize<List<SessionRecord>>(json) ?? new List<SessionRecord>();
            }
            catch (JsonException)
            {
                // A damaged history file mustn't stop the app opening: keep a copy beside it and start fresh.
                try { File.Copy(_filePath, _filePath + $".damaged-{DateTime.Now:yyyyMMdd-HHmmss}", true); }
                catch { /* the copy is a courtesy */ }
                _cache = new List<SessionRecord>();
            }
            _cacheKey = key;
            return _cache;
        }
    }

    private void Save(List<SessionRecord> records)
    {
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        lock (_gate)
        {
            AtomicFile.WriteAllText(_filePath, json);
            var info = new FileInfo(_filePath);
            _cache = records;
            _cacheKey = (info.LastWriteTimeUtc, info.Length);
        }
    }

    public void InsertSession(SessionRecord record)
    {
        var all = LoadAll();
        record.Id = all.Count == 0 ? 1 : all.Max(r => r.Id) + 1;
        all.Add(record);
        Save(all);
    }

    // Sessions started without a project (e.g. from a Stream Deck key) get allocated later.
    // Null projectId clears the assignment.
    public void AssignProject(int sessionId, string? projectId, string? projectName)
    {
        var all = LoadAll();
        var record = all.FirstOrDefault(r => r.Id == sessionId);
        if (record == null) return;

        record.ProjectId = projectId;
        record.ProjectName = projectName;
        Save(all);
    }

    public List<SessionRecord> GetAll()
    {
        return LoadAll().OrderByDescending(r => r.StartTime).ToList();
    }

    public List<SessionRecord> GetForProject(string projectId)
    {
        return LoadAll().Where(r => r.ProjectId == projectId).OrderByDescending(r => r.StartTime).ToList();
    }

    public int GetTodayTotalSeconds()
    {
        return GetTotalSecondsSince(DateTime.Today);
    }

    public Dictionary<DateTime, int> GetLastNDaysTotals(int days)
    {
        var since = DateTime.Today.AddDays(-(days - 1));
        var result = new Dictionary<DateTime, int>();
        for (var d = since; d <= DateTime.Today; d = d.AddDays(1))
            result[d] = 0;

        foreach (var record in LoadAll())
        {
            var day = record.StartTime.Date;
            if (day >= since && result.ContainsKey(day))
                result[day] += record.ActiveSeconds;
        }

        return result;
    }

    private int GetTotalSecondsSince(DateTime since)
    {
        return LoadAll().Where(r => r.StartTime >= since).Sum(r => r.ActiveSeconds);
    }
}
