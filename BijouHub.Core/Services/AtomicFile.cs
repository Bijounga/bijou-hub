using System.IO;

namespace BijouHub.Services;

// Writing straight over a file (File.WriteAllText) leaves it corrupted if the write is
// interrupted partway through — a crash, a force-kill, or (for files living in a synced
// folder like Dropbox/Google Drive) the sync client reading the file mid-write. Writing to
// a temp file first and renaming it into place makes the swap atomic: a reader only ever
// sees the old complete file or the new complete file, never a half-written one.
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, path, overwrite: true);
    }
}
