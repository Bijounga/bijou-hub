namespace BijouHub.Services;

// What a pasted playlist link is, and what to open for it. A Spotify link is tried as a spotify:
// address first, which the Spotify app opens and starts playing; the web link is the fallback for
// when the app isn't there. Anything else (YouTube, ...) opens as the link it is.
public static class MusicLinks
{
    private static readonly string[] SpotifyKinds = { "playlist", "album", "track", "artist", "show", "episode" };
    private static readonly string[] SpotifyPlayable = { "playlist", "album", "track" };

    // Accepts web links and spotify: addresses; false for anything else.
    public static bool TryNormalize(string? text, out string url)
    {
        url = (text ?? "").Trim();
        if (url.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase)) return SpotifyParts(url) != null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return true;

        // "open.spotify.com/..." or "youtube.com/..." pasted without the https://
        if (!url.Contains(' ') && url.Contains('.') && Uri.TryCreate("https://" + url, UriKind.Absolute, out var bare) && bare.Host.Contains('.'))
        {
            url = "https://" + url;
            return true;
        }
        return false;
    }

    public static string SuggestName(string url)
    {
        if (SpotifyParts(url) is { } found) return $"Spotify {found.Kind}";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "Playlist";
        var host = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
        return host switch
        {
            "youtube.com" or "youtu.be" => "YouTube playlist",
            "music.youtube.com" => "YouTube Music playlist",
            "soundcloud.com" => "SoundCloud playlist",
            _ => host
        };
    }

    // Things to try opening, best first.
    public static IReadOnlyList<string> LaunchTargets(string url)
    {
        if (SpotifyParts(url) is not { } spotify) return new[] { url };

        var (kind, id) = spotify;
        var address = $"spotify:{kind}:{id}" + (Array.IndexOf(SpotifyPlayable, kind) >= 0 ? ":play" : "");
        return new[] { address, $"https://open.spotify.com/{kind}/{id}" };
    }

    // ("playlist", "37i9dQZF1DX...") from either spotify:playlist:ID or an open.spotify.com link.
    private static (string Kind, string Id)? SpotifyParts(string url)
    {
        if (url.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = url.Split(':');
            // spotify:playlist:ID, or spotify:user:someone:playlist:ID
            for (var i = 1; i < parts.Length - 1; i++)
                if (Array.IndexOf(SpotifyKinds, parts[i].ToLowerInvariant()) >= 0 && parts[i + 1].Length > 0)
                    return (parts[i].ToLowerInvariant(), parts[i + 1]);
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("open.spotify.com", StringComparison.OrdinalIgnoreCase))
            return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
            if (Array.IndexOf(SpotifyKinds, segments[i].ToLowerInvariant()) >= 0)
                return (segments[i].ToLowerInvariant(), segments[i + 1]);
        return null;
    }
}
