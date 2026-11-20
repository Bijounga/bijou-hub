using System.Diagnostics;
using System.IO;

namespace BijouHub.Services;

// Opening BijouHub asks the running copy to show its window. A copy that never answers, and isn't
// quitting either, is frozen: this ends it (what ending it in Task Manager does) so the one being
// opened can take over, instead of the new one giving up and leaving nothing on screen.
public static class StuckInstance
{
    public static bool Replace(SingleInstance instance)
    {
        var path = Environment.ProcessPath;
        if (path == null) return false;

        using var self = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            using (other)
            {
                try
                {
                    // Only a copy of this same BijouHub, in this sign-in: never another program, or a test copy elsewhere.
                    if (other.Id == self.Id || other.SessionId != self.SessionId) continue;
                    if (!string.Equals(other.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) continue;

                    ErrorLog.Write($"BijouHub (process {other.Id}) wasn't answering when it was opened again, so it was ended and replaced.");
                    other.Kill();
                    other.WaitForExit(5000);
                }
                catch
                {
                    // already gone, or not ours to end
                }
            }
        }
        return instance.TakeOver(TimeSpan.FromSeconds(5));
    }
}
