using System.IO;
using System.Text.Json;
using BijouHub.Models;

namespace BijouHub.Services;

public class QuickLaunchStore
{
    private readonly string _filePath;

    public QuickLaunchStore()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BijouHub");
        Directory.CreateDirectory(dir);
        _filePath = System.IO.Path.Combine(dir, "quicklaunch.json");
    }

    public List<QuickLaunchApp> Load()
    {
        if (!File.Exists(_filePath))
            return new List<QuickLaunchApp>();

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<QuickLaunchApp>();

        return JsonSerializer.Deserialize<List<QuickLaunchApp>>(json) ?? new List<QuickLaunchApp>();
    }

    public void Save(List<QuickLaunchApp> apps)
    {
        var json = JsonSerializer.Serialize(apps, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
