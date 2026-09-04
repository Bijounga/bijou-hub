using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace BijouHub.Mac.Services;

public record MacUpdateInfo(string Version, string DownloadUrl);

public static class MacUpdateService
{
    private const string RepoOwner = "Bijounga";
    private const string RepoName = "bijou-hub";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BijouHub-Mac-Updater", GetCurrentVersion()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static string GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static string DmgAssetName =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "BijouHub-osx-arm64.dmg"
            : "BijouHub-osx-x64.dmg";

    public static async Task<MacUpdateInfo?> CheckForUpdateAsync()
    {
        var json = await Http.GetStringAsync(
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");

        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var latestVersionText = tag.TrimStart('v', 'V');

        if (!Version.TryParse(latestVersionText, out var latest)) return null;
        if (!Version.TryParse(GetCurrentVersion(), out var current)) return null;
        if (latest <= current) return null;

        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.Equals(DmgAssetName, StringComparison.OrdinalIgnoreCase))
                return new MacUpdateInfo(latestVersionText, asset.GetProperty("browser_download_url").GetString()!);
        }
        return null;
    }

    // Downloads the new DMG and opens it with Finder's normal DMG handling (mount + show),
    // exactly like double-clicking a downloaded installer. The user drags the app into
    // Applications themselves, same as any other unsigned Mac app update.
    public static async Task DownloadAndOpenAsync(string downloadUrl)
    {
        var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloadsDir))
            downloadsDir = Path.GetTempPath();

        var dmgPath = Path.Combine(downloadsDir, DmgAssetName);

        var bytes = await Http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(dmgPath, bytes);

        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"\"{dmgPath}\"",
            UseShellExecute = false
        });
    }
}
