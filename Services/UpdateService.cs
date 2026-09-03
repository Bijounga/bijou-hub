using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace BijouHub.Services;

public record UpdateInfo(string Version, string DownloadUrl);

public static class UpdateService
{
    private const string RepoOwner = "Bijounga";
    private const string RepoName = "bijou-hub";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BijouHub-Updater", GetCurrentVersion()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static string GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
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
                if (name.Equals("BijouHub.exe", StringComparison.OrdinalIgnoreCase))
                    return new UpdateInfo(latestVersionText, asset.GetProperty("browser_download_url").GetString()!);
            }
            return null;
        }
        catch
        {
            // No network, no releases yet, rate-limited, etc. — silently skip the check.
            return null;
        }
    }

    // Downloads the new exe, then hands off to a spawned script that waits for this process
    // to exit before swapping the file in place, since Windows won't let a running exe
    // overwrite itself.
    public static async Task DownloadAndApplyAsync(string downloadUrl)
    {
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path");
        var newExePath = Path.Combine(Path.GetTempPath(), "BijouHub_update.exe");

        var bytes = await Http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(newExePath, bytes);

        var scriptPath = Path.Combine(Path.GetTempPath(), "bijouhub_apply_update.bat");
        var pid = Environment.ProcessId;
        var script = $"""
            @echo off
            :wait
            tasklist /fi "PID eq {pid}" | find "{pid}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto wait
            )
            move /y "{newExePath}" "{exePath}" >nul
            start "" "{exePath}"
            del "%~f0"
            """;
        await File.WriteAllTextAsync(scriptPath, script);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = scriptPath,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        });

        System.Windows.Application.Current.Shutdown();
    }
}
