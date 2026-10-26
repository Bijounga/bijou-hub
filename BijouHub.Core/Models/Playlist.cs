namespace BijouHub.Models;

// A saved link to a playlist (Spotify, YouTube, ...), played in whatever app opens that link.
public class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    public override string ToString() => Name;
}
