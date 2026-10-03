using System.Media;
using System.Windows;

namespace BijouHub.Services;

// BijouHub's own chimes (Assets/Sounds, soft bells) for timer moments: start, a Pomodoro's
// focus and break, time's up, all rounds done, and task reminders. Loaded once, played without
// waiting.
public static class SoundFx
{
    public const string Start = "start", Focus = "focus", Break = "break", End = "end", Complete = "complete", Reminder = "reminder";

    private static readonly Dictionary<string, SoundPlayer> Players = new();

    public static void Play(string name)
    {
        try
        {
            if (!Players.TryGetValue(name, out var player))
            {
                var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Sounds/{name}.wav"));
                if (resource == null) return;
                player = new SoundPlayer(resource.Stream);
                player.Load();
                Players[name] = player;
            }
            player.Play();
        }
        catch
        {
            // No sound device, or the file's missing: the timer works the same without it.
        }
    }
}
