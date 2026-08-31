using System.IO;
using System.Text.Json;
using BijouHub.Models;

namespace BijouHub.Services;

public class ModeStore
{
    private readonly string _filePath;

    public ModeStore()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BijouHub");
        Directory.CreateDirectory(dir);
        _filePath = System.IO.Path.Combine(dir, "modes.json");
    }

    public List<WorkMode> Load()
    {
        if (!File.Exists(_filePath))
            return new List<WorkMode>();

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<WorkMode>();

        return JsonSerializer.Deserialize<List<WorkMode>>(json) ?? new List<WorkMode>();
    }

    public void Save(List<WorkMode> modes)
    {
        var json = JsonSerializer.Serialize(modes, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
