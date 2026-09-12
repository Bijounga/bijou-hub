using System.IO;
using System.Text.Json;
using BijouHub.Models;

namespace BijouHub.Services;

public class ProjectStore
{
    private readonly string _filePath;

    public ProjectStore()
    {
        _filePath = System.IO.Path.Combine(DataPaths.SyncDir, "projects.json");
    }

    public List<Project> Load()
    {
        if (!File.Exists(_filePath))
            return new List<Project>();

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<Project>();

        return JsonSerializer.Deserialize<List<Project>>(json) ?? new List<Project>();
    }

    public void Save(List<Project> projects)
    {
        var json = JsonSerializer.Serialize(projects, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(_filePath, json);
    }
}
