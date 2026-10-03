namespace BijouHub.Models;

public enum LaunchItemType
{
    Application,
    Folder,
    File,
    Url,
    BrowserUrl
}

public class LaunchItem
{
    public LaunchItemType Type { get; set; }
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string? Browser { get; set; }
    public string? BrowserProfileDir { get; set; }
    public string? BrowserProfileLabel { get; set; }
    public int DelayMs { get; set; } = 0;
    public string? OpenWithAppPath { get; set; }

    public string DisplayName => Type switch
    {
        LaunchItemType.Application => System.IO.Path.GetFileName(Path),
        LaunchItemType.Folder => $"Folder: {Path}{OpenWithSuffix}",
        LaunchItemType.File => $"File: {Path}{OpenWithSuffix}",
        LaunchItemType.Url => $"Link: {Path}",
        LaunchItemType.BrowserUrl => $"Link ({Browser}{(string.IsNullOrEmpty(BrowserProfileLabel) ? "" : ", " + BrowserProfileLabel)}): {Path}",
        _ => Path
    };

    private string OpenWithSuffix => string.IsNullOrEmpty(OpenWithAppPath)
        ? ""
        : $" (via {System.IO.Path.GetFileNameWithoutExtension(OpenWithAppPath)})";
}
