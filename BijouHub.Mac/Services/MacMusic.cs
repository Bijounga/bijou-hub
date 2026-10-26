using System.Diagnostics;
using BijouHub.Models;

namespace BijouHub.Mac.Services;

// Opens a playlist link in the default browser. Pausing it is left to the keyboard's play/pause key:
// macOS gives an app no way to pause another app's audio.
public static class MacMusic
{
    public static void Play(Playlist playlist)
    {
        try
        {
            Process.Start(OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open") { ArgumentList = { playlist.Url } }
                : new ProcessStartInfo(playlist.Url) { UseShellExecute = true });
        }
        catch
        {
            // nothing to open the link with
        }
    }
}
