namespace BijouHub.Services;

// What a pasted playlist link is: a web link, opened in the browser.
public static class MusicLinks
{
    // Accepts web links, with or without the https://; false for anything else.
    public static bool TryNormalize(string? text, out string url)
    {
        url = (text ?? "").Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return true;

        // "youtube.com/..." pasted without the https://
        if (!url.Contains(' ') && url.Contains('.') && Uri.TryCreate("https://" + url, UriKind.Absolute, out var bare) && bare.Host.Contains('.'))
        {
            url = "https://" + url;
            return true;
        }
        return false;
    }

    public static string SuggestName(string url)
    {
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
}
