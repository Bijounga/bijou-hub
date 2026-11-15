using System.IO;

namespace BijouHub.Services;

// The notes widget's text: one plain file in the sync folder, so what you jotted on one computer is
// there on the other. Saved in one piece and atomically; Changed says whether the file was written
// since a time (by another computer, say) so the widget can pick the new text up.
public class NotesStore
{
    private readonly string _file = Path.Combine(DataPaths.SyncDir, "notes.txt");

    public string Load() => File.Exists(_file) ? File.ReadAllText(_file) : "";

    public void Save(string text) => AtomicFile.WriteAllText(_file, text);

    public DateTime LastWriteUtc => File.Exists(_file) ? File.GetLastWriteTimeUtc(_file) : DateTime.MinValue;
}
