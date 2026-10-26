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
