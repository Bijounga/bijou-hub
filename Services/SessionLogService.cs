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

    private List<SessionRecord> LoadAll()
    {
        if (!File.Exists(_filePath)) return new List<SessionRecord>();

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json)) return new List<SessionRecord>();

        return JsonSerializer.Deserialize<List<SessionRecord>>(json) ?? new List<SessionRecord>();
    }

    private void Save(List<SessionRecord> records)
    {
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    public void InsertSession(SessionRecord record)
    {
        var all = LoadAll();
        record.Id = all.Count == 0 ? 1 : all.Max(r => r.Id) + 1;
        all.Add(record);
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
