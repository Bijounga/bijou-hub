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
        EjectStaleMounts();

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

    // Every `open some.dmg` mounts a fresh /Volumes/BijouHub. If a previous update's volume
    // was never ejected (the user just drags the app out and closes the Finder window, not
    // the mount), macOS starts numbering duplicates — "BijouHub 1", "BijouHub 2" — that pile
    // up forever. Eject anything already mounted under that name before mounting a new one.
    // Safe to call anytime, including at startup to clean up leftovers from before this existed.
    public static void EjectStaleMounts()
    {
        const string volumesDir = "/Volumes";
        if (!Directory.Exists(volumesDir)) return;

        foreach (var dir in Directory.GetDirectories(volumesDir))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith("BijouHub", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "hdiutil",
                    Arguments = $"detach \"{dir}\" -quiet -force",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                p?.WaitForExit(5000);
            }
            catch
            {
                // Best-effort cleanup — a stuck mount shouldn't block the update.
            }
        }
    }
}
