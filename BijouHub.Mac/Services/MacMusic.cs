using System.Diagnostics;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac.Services;

// Starts a playlist in whatever app opens its link (Spotify, the browser for YouTube) and pauses
// Spotify again. macOS has no shared media controls an app can pause, so a playlist in a browser
// tab is paused with the keyboard's play/pause key (or the tab) rather than by BijouHub.
public static class MacMusic
{
    public static Task PlayAsync(Playlist playlist) => Task.Run(() =>
    {
        PauseSpotify();
        foreach (var target in MusicLinks.LaunchTargets(playlist.Url))
            if (Open(target)) return;
    });

    public static Task PauseAsync() => Task.Run(PauseSpotify);

    // `open` fails (non-zero) when nothing handles the address, e.g. spotify: without the Spotify app.
    private static bool Open(string target)
    {
        try
        {
            var start = OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open") { ArgumentList = { target } }
                : new ProcessStartInfo(target) { UseShellExecute = true };
            using var process = Process.Start(start);
            if (process == null || !OperatingSystem.IsMacOS()) return process != null;
            return process.WaitForExit(4000) ? process.ExitCode == 0 : true;
        }
        catch
        {
            return false;
        }
    }

    // Only if Spotify is already running, so it's never launched just to be told to pause.
    private static void PauseSpotify()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var start = new ProcessStartInfo("osascript")
            {
                ArgumentList = { "-e", "if application \"Spotify\" is running then tell application \"Spotify\" to pause" },
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(start);
            process?.WaitForExit(3000);
        }
        catch
        {
            // no Spotify, nothing to pause
        }
    }
}
