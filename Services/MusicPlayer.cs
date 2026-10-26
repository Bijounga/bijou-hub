using System.ComponentModel;
using System.Diagnostics;
using BijouHub.Models;
using Windows.Media.Control;

namespace BijouHub.Services;

// Starts a playlist in whatever app opens its link (Spotify, a browser tab for YouTube) and pauses
// it again. Pausing goes through Windows' media controls, the same thing the keyboard's play/pause
// key talks to, and only acts if something is actually playing, so it never starts music by mistake.
public static class MusicPlayer
{
    // Whatever is playing stops first, so a break's playlist doesn't land on top of the work one.
    public static async Task PlayAsync(Playlist playlist)
    {
        await PauseAsync();
        foreach (var target in MusicLinks.LaunchTargets(playlist.Url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return;
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                // no app for that address (Spotify isn't installed): the next one is the web link
            }
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
