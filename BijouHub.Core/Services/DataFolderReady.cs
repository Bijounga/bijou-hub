using System.IO;

namespace BijouHub.Services;

// The folder BijouHub keeps its data in can be a cloud drive's (Google Drive, OneDrive...). Right after
// signing in it may not be there yet, or may hold a request for a long while, and BijouHub reads its
// data on the window's own thread, which would leave it frozen with nothing on screen. So the folder is
// asked first, from another thread, and BijouHub only goes on once it has answered.
public static class DataFolderReady
{
    // True once the folder answers. `patient` keeps asking for ten minutes (started at sign-in, nobody is
    // waiting); otherwise for `give` (a person opened it and is waiting for a window). No folder set
    // means the local one, which is always there.
    public static async Task<bool> WaitAsync(string? folder, bool patient, TimeSpan? give = null, TimeSpan? ask = null)
    {
        if (string.IsNullOrWhiteSpace(folder)) return true;

        var deadline = DateTime.UtcNow + (patient ? TimeSpan.FromMinutes(10) : give ?? TimeSpan.FromSeconds(25));
        while (true)
        {
            if (await AnswersAsync(folder, ask ?? TimeSpan.FromSeconds(4))) return true;
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    // One question on its own thread. A call that never comes back is left behind there; the next
    // question is a fresh one (a drive that was stuck on the first often answers the second).
    private static async Task<bool> AnswersAsync(string folder, TimeSpan within)
    {
        var question = Task.Run(() =>
        {
            try { return Directory.Exists(folder); }
            catch { return false; }
        });
        return await Task.WhenAny(question, Task.Delay(within)) == question && question.Result;
    }
}
