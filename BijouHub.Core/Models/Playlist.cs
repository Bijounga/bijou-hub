namespace BijouHub.Models;

// A saved link to a playlist (YouTube, ...), played in the browser.
public class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    public override string ToString() => Name;
}
