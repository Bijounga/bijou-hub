using System.IO;
using System.Text.Json;

namespace BijouHub.Services;

public class BrowserProfile
{
    public string DirectoryName { get; set; } = "";
    public string Label { get; set; } = "";

    public override string ToString() => Label;
}

public static class BrowserProfileService
{
    public static readonly Dictionary<string, string> KnownBrowserExePaths = new();

    static BrowserProfileService()
    {
        var candidates = new Dictionary<string, string[]>
        {
            ["Chrome"] = new[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
            },
            ["Edge"] = new[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
            }
        };

        foreach (var (browser, paths) in candidates)
        {
            var found = paths.FirstOrDefault(File.Exists);
            if (found != null)
                KnownBrowserExePaths[browser] = found;
        }
    }

    private static string? GetLocalStatePath(string browser)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return browser switch
        {
            "Chrome" => System.IO.Path.Combine(localAppData, @"Google\Chrome\User Data\Local State"),
            "Edge" => System.IO.Path.Combine(localAppData, @"Microsoft\Edge\User Data\Local State"),
            _ => null
        };
    }

    public static List<BrowserProfile> GetProfiles(string browser)
    {
        var result = new List<BrowserProfile>();
        var localStatePath = GetLocalStatePath(browser);
        if (localStatePath == null || !File.Exists(localStatePath))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
            if (doc.RootElement.TryGetProperty("profile", out var profileEl) &&
                profileEl.TryGetProperty("info_cache", out var infoCache))
            {
                foreach (var entry in infoCache.EnumerateObject())
                {
                    var dirName = entry.Name;
                    var label = dirName;
                    if (entry.Value.TryGetProperty("name", out var nameEl))
                        label = nameEl.GetString() ?? dirName;

                    result.Add(new BrowserProfile { DirectoryName = dirName, Label = label });
                }
            }
        }
        catch
        {
            // Local State is unreadable/locked - just return what we have (nothing)
        }

        return result;
    }

    public static void LaunchUrl(string? browser, string? profileDir, string url)
    {
        if (string.IsNullOrEmpty(browser) || !KnownBrowserExePaths.TryGetValue(browser, out var exePath))
        {
            // Fall back to system default browser
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return;
        }

        var args = string.IsNullOrEmpty(profileDir)
            ? $"\"{url}\""
            : $"--profile-directory=\"{profileDir}\" \"{url}\"";

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = true
        });
    }
}
