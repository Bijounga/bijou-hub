namespace BijouHub.Services;

// What a pasted playlist link is: a web link, opened in the browser.
public static class MusicLinks
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    // The title YouTube has for a video or playlist link, or null (not a YouTube link, offline, ...).
    public static async Task<string?> FetchTitleAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
        if (host is not ("youtube.com" or "youtu.be" or "music.youtube.com" or "m.youtube.com")) return null;

        try
        {
            var json = await Http.GetStringAsync("https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(url));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("title", out var title) && title.GetString()?.Trim() is { Length: > 0 } text ? text : null;
        }
        catch
        {
            return null;
        }
    }

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
