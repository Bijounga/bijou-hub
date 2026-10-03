using System.Diagnostics;
using Avalonia.Platform;

namespace BijouHub.Mac.Services;

// BijouHub's own chimes (soft bells, shared with the Windows app) for timer moments: start, a
// Pomodoro's focus and break, time's up, all rounds done, and task reminders. Each is copied out
// of the app once and played with afplay, without waiting.
public static class SoundFx
{
    public const string Start = "start", Focus = "focus", Break = "break", End = "end", Complete = "complete", Reminder = "reminder";

    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "bijouhub-sounds");

    public static void Play(string name)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var file = Path.Combine(Folder, name + ".wav");
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Folder);
                using var source = AssetLoader.Open(new Uri($"avares://BijouHub.Mac/Assets/Sounds/{name}.wav"));
                using var target = File.Create(file);
                source.CopyTo(target);
            }
            Process.Start(new ProcessStartInfo("/usr/bin/afplay", $"\"{file}\"") { UseShellExecute = false });
        }
        catch
        {
            // No sound: the timer works the same without it.
        }
    }
}
