namespace BijouHub.Models;

// A saved link to a playlist (YouTube, ...), played in the browser.
public enum PlaylistKind { Work, Break }

public class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    // Which tab it's on: played while working, or on a Pomodoro break.
    public PlaylistKind Kind { get; set; } = PlaylistKind.Work;

    public override string ToString() => Name;
}
