using System.ComponentModel;
using System.Diagnostics;
using BijouHub.Models;
using Windows.Media.Control;

namespace BijouHub.Services;

// Opens a playlist link in the browser and pauses it again. Pausing goes through Windows' media
// controls, the same thing the keyboard's play/pause key talks to, and only acts if something is
// actually playing, so it never starts music by mistake.
public static class MusicPlayer
{
    // Whatever is playing stops first, so a break's playlist doesn't land on top of the work one.
    public static async Task PlayAsync(Playlist playlist)
    {
        await PauseAsync();
        try
        {
            Process.Start(new ProcessStartInfo(playlist.Url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // nothing to open the link with
        }
    }

    // The current media session is at the end of what it's playing (a long video that has run out),
    // so the next playlist should start. Never true for a short clip or a paused one mid-way.
    public static async Task<bool> HasEndedAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (manager.GetCurrentSession() is not { } session) return false;

            var info = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var length = timeline.EndTime - timeline.StartTime;
            if (length < TimeSpan.FromSeconds(30)) return false;

            var position = timeline.Position - timeline.StartTime;
            var playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (playing)
            {
                // the position was reported a moment ago, and has moved on since
                var age = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                if (age > TimeSpan.Zero && age < TimeSpan.FromSeconds(15)) position += age * (info.PlaybackRate ?? 1);
            }

            var left = length - position;
            return left <= TimeSpan.FromSeconds(3) || (!playing && left <= TimeSpan.FromSeconds(8));
        }
        catch
        {
            return false;
        }
    }

    public static async Task PauseAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session?.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                await session.TryPauseAsync();
        }
        catch
        {
            // media controls unavailable: nothing to pause
        }
    }
}
