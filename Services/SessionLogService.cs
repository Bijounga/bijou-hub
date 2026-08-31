using System.IO;
using Microsoft.Data.Sqlite;
using BijouHub.Models;

namespace BijouHub.Services;

public class SessionLogService
{
    private readonly string _connectionString;

    public SessionLogService()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BijouHub");
        Directory.CreateDirectory(dir);
        var dbPath = System.IO.Path.Combine(dir, "sessions.db");
        _connectionString = $"Data Source={dbPath}";
        Init();
    }

    private void Init()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Sessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ModeName TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT NOT NULL,
                ActiveSeconds INTEGER NOT NULL,
                IdleSeconds INTEGER NOT NULL
            );";
        cmd.ExecuteNonQuery();

        MigrateAddColumnIfMissing(conn, "ProjectId", "TEXT");
        MigrateAddColumnIfMissing(conn, "ProjectName", "TEXT");
        MigrateAddColumnIfMissing(conn, "GoalId", "TEXT");
        MigrateAddColumnIfMissing(conn, "GoalName", "TEXT");
        MigrateAddColumnIfMissing(conn, "Note", "TEXT");
    }

    private static void MigrateAddColumnIfMissing(SqliteConnection conn, string columnName, string sqlType)
    {
        var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "PRAGMA table_info(Sessions);";
        using (var reader = checkCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        var alterCmd = conn.CreateCommand();
        alterCmd.CommandText = $"ALTER TABLE Sessions ADD COLUMN {columnName} {sqlType};";
        alterCmd.ExecuteNonQuery();
    }

    public void InsertSession(SessionRecord record)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Sessions (ModeName, StartTime, EndTime, ActiveSeconds, IdleSeconds, ProjectId, ProjectName, GoalId, GoalName, Note)
            VALUES ($modeName, $startTime, $endTime, $activeSeconds, $idleSeconds, $projectId, $projectName, $goalId, $goalName, $note);";
        cmd.Parameters.AddWithValue("$modeName", record.ModeName);
        cmd.Parameters.AddWithValue("$startTime", record.StartTime.ToString("o"));
        cmd.Parameters.AddWithValue("$endTime", record.EndTime.ToString("o"));
        cmd.Parameters.AddWithValue("$activeSeconds", record.ActiveSeconds);
        cmd.Parameters.AddWithValue("$idleSeconds", record.IdleSeconds);
        cmd.Parameters.AddWithValue("$projectId", (object?)record.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$projectName", (object?)record.ProjectName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$goalId", (object?)record.GoalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$goalName", (object?)record.GoalName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$note", (object?)record.Note ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<SessionRecord> GetAll()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, ModeName, StartTime, EndTime, ActiveSeconds, IdleSeconds,
                                    ProjectId, ProjectName, GoalId, GoalName, Note
                             FROM Sessions ORDER BY StartTime DESC;";
        return ReadAll(cmd);
    }

    public List<SessionRecord> GetForProject(string projectId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, ModeName, StartTime, EndTime, ActiveSeconds, IdleSeconds,
                                    ProjectId, ProjectName, GoalId, GoalName, Note
                             FROM Sessions WHERE ProjectId = $projectId ORDER BY StartTime DESC;";
        cmd.Parameters.AddWithValue("$projectId", projectId);
        return ReadAll(cmd);
    }

    public int GetTodayTotalSeconds()
    {
        var todayStart = DateTime.Today;
        return GetTotalSecondsSince(todayStart);
    }

    public Dictionary<DateTime, int> GetLastNDaysTotals(int days)
    {
        var since = DateTime.Today.AddDays(-(days - 1));
        var result = new Dictionary<DateTime, int>();
        for (var d = since; d <= DateTime.Today; d = d.AddDays(1))
            result[d] = 0;

        foreach (var record in GetAll())
        {
            var day = record.StartTime.Date;
            if (day >= since && result.ContainsKey(day))
                result[day] += record.ActiveSeconds;
        }

        return result;
    }

    private int GetTotalSecondsSince(DateTime since)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(ActiveSeconds), 0) FROM Sessions WHERE StartTime >= $since;";
        cmd.Parameters.AddWithValue("$since", since.ToString("o"));
        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    private static List<SessionRecord> ReadAll(SqliteCommand cmd)
    {
        var result = new List<SessionRecord>();
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
}
