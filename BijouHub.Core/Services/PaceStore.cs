using System.IO;
using System.Text.Json;
using BijouHub.Models;

namespace BijouHub.Services;

// Paces live beside the projects and session history, so they follow the user between machines.
public class PaceStore
{
    private readonly string _filePath = Path.Combine(DataPaths.SyncDir, "paces.json");

    public List<TaskPace> Load()
    {
        if (!File.Exists(_filePath)) return new List<TaskPace>();
        try
        {
            var json = File.ReadAllText(_filePath);
            return string.IsNullOrWhiteSpace(json)
                ? new List<TaskPace>()
                : JsonSerializer.Deserialize<List<TaskPace>>(json) ?? new List<TaskPace>();
        }
        catch (JsonException)
        {
            return new List<TaskPace>();
        }
    }

    public void Save(List<TaskPace> paces)
    {
        var json = JsonSerializer.Serialize(paces, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(_filePath, json);
    }
}
