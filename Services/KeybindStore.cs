using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace BijouHub.Services;

public class KeybindStore
{
    private readonly string _filePath;
    private Dictionary<string, string> _binds;

    public static readonly Dictionary<string, string> Defaults = new()
    {
        ["Bold"] = "Ctrl+B",
        ["Italic"] = "Ctrl+I",
        ["Heading"] = "Ctrl+Shift+H",
        ["BulletList"] = "Ctrl+Shift+L",
        ["Checklist"] = "Ctrl+Shift+C"
    };

    public KeybindStore()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BijouHub");
        Directory.CreateDirectory(dir);
        _filePath = System.IO.Path.Combine(dir, "keybinds.json");
        _binds = Load();
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, string>(Defaults);

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (loaded == null) return new Dictionary<string, string>(Defaults);

            var result = new Dictionary<string, string>(Defaults);
            foreach (var kv in loaded)
                result[kv.Key] = kv.Value;
            return result;
        }
        catch
        {
            return new Dictionary<string, string>(Defaults);
        }
    }

    public void Save()
    {
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(_binds, new JsonSerializerOptions { WriteIndented = true }));
    }

    public string Get(string action) => _binds.TryGetValue(action, out var gesture) ? gesture : Defaults[action];

    public void Set(string action, string gesture)
    {
        _binds[action] = gesture;
        Save();
    }

    public static KeyGesture? ParseGesture(string text)
    {
        try
        {
            var converter = new KeyGestureConverter();
            return converter.ConvertFromString(text) as KeyGesture;
        }
        catch
        {
            return null;
        }
    }
}
