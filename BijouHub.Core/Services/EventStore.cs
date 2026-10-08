using System.IO;
using System.Text.Json;
using BijouHub.Models;

namespace BijouHub.Services;

// Calendar events, kept in the sync folder with the rest of the user's data so they follow
// between machines. Small, so every change writes the whole file.
public class EventStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly string _filePath = Path.Combine(DataPaths.SyncDir, "events.json");

    public List<CalendarEvent> Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new List<CalendarEvent>();
            return JsonSerializer.Deserialize<List<CalendarEvent>>(File.ReadAllText(_filePath)) ?? new List<CalendarEvent>();
        }
        catch (JsonException)
        {
            // A damaged file mustn't stop the app opening: keep a copy beside it and start fresh.
            try { File.Copy(_filePath, _filePath + $".damaged-{DateTime.Now:yyyyMMdd-HHmmss}", true); }
            catch { /* the copy is a courtesy */ }
            return new List<CalendarEvent>();
        }
        catch (IOException)
        {
            return new List<CalendarEvent>();
        }
    }

    public void Save(IEnumerable<CalendarEvent> events)
    {
        var ordered = events.OrderBy(e => e.Start).ToList();
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(ordered, WriteOptions));
    }
}
