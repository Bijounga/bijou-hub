using System.IO;
using System.Text.Json;
using BijouHub.Models;

namespace BijouHub.Services;

// Playlists live beside the projects, so they follow the user between machines.
public class PlaylistStore
{
    private readonly string _filePath = Path.Combine(DataPaths.SyncDir, "playlists.json");

    public List<Playlist> Load()
    {
        if (!File.Exists(_filePath)) return new List<Playlist>();
        try
        {
            var json = File.ReadAllText(_filePath);
            return string.IsNullOrWhiteSpace(json)
                ? new List<Playlist>()
                : JsonSerializer.Deserialize<List<Playlist>>(json) ?? new List<Playlist>();
        }
        catch (JsonException)
        {
            return new List<Playlist>();
        }
    }

    public void Save(List<Playlist> playlists)
    {
        var json = JsonSerializer.Serialize(playlists, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(_filePath, json);
    }
}
