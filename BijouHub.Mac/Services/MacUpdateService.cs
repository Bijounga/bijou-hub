using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace BijouHub.Mac.Services;

public record MacUpdateInfo(string Version, string ZipUrl);

// One-click updates that replace the app in place (the same approach as Bijou Footage):
//   - each Release carries a .zip of BijouHub.app per architecture next to the .dmg;
//   - the update downloads that zip, unpacks it with ditto into a temp folder and checks the
//     new app's signature;
//   - "Restart to update" hands off to a tiny script that waits for BijouHub to quit, swaps the
//     new app into place, clears the download quarantine flag, reopens it and cleans up.
// No .dmg is ever mounted for an update, so nothing piles up in Finder's sidebar; leftovers
// from the first install are ejected at launch. Running from the disk image itself, BijouHub
// offers to move into Applications first (an app inside a read-only image can't replace itself).
public static class MacUpdateService
{
    private const string RepoOwner = "Bijounga";
    private const string RepoName = "bijou-hub";
    private const string TempPrefix = "bijouhub-update-";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BijouHub-Mac-Updater", GetCurrentVersion()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static string GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    // …/BijouHub.app when running from a bundle, else null (e.g. a dev run).
    public static string? BundlePath
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return null;
            var macOs = Path.GetDirectoryName(exe);                  // …/BijouHub.app/Contents/MacOS
            var bundle = Path.GetDirectoryName(Path.GetDirectoryName(macOs)); // …/BijouHub.app
            return bundle != null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? bundle : null;
        }
    }

    public static bool RunningFromDiskImage => BundlePath?.StartsWith("/Volumes/", StringComparison.Ordinal) == true;

    // Whether this copy can swap itself out: a real bundle we can write next to, not inside a DMG.
    public static bool CanSelfUpdate
    {
        get
        {
            var bundle = BundlePath;
            if (bundle == null || RunningFromDiskImage) return false;
            try
            {
                var probe = Path.Combine(Path.GetDirectoryName(bundle)!, $".bijouhub-write-test-{Environment.ProcessId}");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static async Task<MacUpdateInfo?> CheckForUpdateAsync()
    {
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var latestText = tag.TrimStart('v', 'V');
        if (!Version.TryParse(latestText, out var latest) || !Version.TryParse(GetCurrentVersion(), out var current) || latest <= current)
            return null;

        var wanted = $"BijouHub-osx-{Arch}.zip";
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (string.Equals(asset.GetProperty("name").GetString(), wanted, StringComparison.OrdinalIgnoreCase))
                return new MacUpdateInfo(latestText, asset.GetProperty("browser_download_url").GetString()!);
        }
        return null;
    }

    // Downloads and unpacks the update; returns the path of the new BijouHub.app, ready to swap in.
    public static async Task<string> DownloadAsync(MacUpdateInfo update, IProgress<int>? progress = null)
    {
        var dir = Directory.CreateTempSubdirectory(TempPrefix).FullName;
        try
        {
            var zip = Path.Combine(dir, "update.zip");
            using (var response = await Http.GetAsync(update.ZipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(zip);
                var buffer = new byte[81920];
                long got = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    got += read;
                    if (total > 0) progress?.Report((int)(got * 100 / total));
                }
            }

            var unpacked = Path.Combine(dir, "app");
            await RunAsync("/usr/bin/ditto", "-x", "-k", zip, unpacked);
            File.Delete(zip);

            var newApp = Path.Combine(unpacked, "BijouHub.app");
            if (!Directory.Exists(newApp)) throw new InvalidOperationException("The download had no BijouHub.app in it.");
            // Not --strict: .NET keeps its non-Mach-O files in Contents/MacOS, which strict mode rejects.
            await RunAsync("/usr/bin/codesign", "--verify", "--deep", newApp);
            return newApp;
        }
        catch
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
            throw;
        }
    }

    // Starts the swap script and returns; the caller then quits the app.
    public static void InstallAndRelaunch(string newApp)
    {
        var target = BundlePath ?? throw new InvalidOperationException("Not running from an app bundle.");
        var dir = Path.GetDirectoryName(Path.GetDirectoryName(newApp))!;
        var script = Path.Combine(dir, "swap.sh");
        File.WriteAllText(script, string.Join('\n',
            "#!/bin/sh",
            "PID=\"$1\"; OLD=\"$2\"; NEW=\"$3\"; TMP=\"$4\"",
            "i=0; while kill -0 \"$PID\" 2>/dev/null && [ $i -lt 300 ]; do sleep 0.2; i=$((i+1)); done",
            "if mv \"$OLD\" \"$TMP/old.app\" && mv \"$NEW\" \"$OLD\"; then",
            "  rm -rf \"$TMP/old.app\"",
            "else",
            "  [ -d \"$OLD\" ] || mv \"$TMP/old.app\" \"$OLD\"  # put the old one back",
            "fi",
            "xattr -dr com.apple.quarantine \"$OLD\" 2>/dev/null",
            "open \"$OLD\"",
            "rm -rf \"$TMP\"",
            ""));

        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var arg in new[] { script, Environment.ProcessId.ToString(), target, newApp, dir }) start.ArgumentList.Add(arg);
        Process.Start(start);
    }

    // ---------- Launch-time tidying ----------

    // Deletes update downloads that never got installed, and ejects BijouHub disk images left
    // mounted from installing (each `open BijouHub.dmg` adds another "BijouHub N" to Finder).
    public static void TidyOnLaunch()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), TempPrefix + "*"))
                Directory.Delete(dir, true);
        }
        catch
        {
            // nothing to tidy
        }

        if (RunningFromDiskImage || !Directory.Exists("/Volumes")) return;
        foreach (var volume in Directory.GetDirectories("/Volumes"))
        {
            if (!Path.GetFileName(volume).StartsWith("BijouHub", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Directory.Exists(Path.Combine(volume, "BijouHub.app"))) continue; // someone else's drive
            try { RunAsync("/usr/bin/hdiutil", "detach", volume, "-quiet", "-force").Wait(10000); }
            catch { /* best effort */ }
        }
    }

    // Copies the running app from the disk image into /Applications and opens that copy.
    public static async Task MoveToApplicationsAsync()
    {
        var bundle = BundlePath ?? throw new InvalidOperationException("Not running from an app bundle.");
        const string target = "/Applications/BijouHub.app";
        if (Directory.Exists(target)) await RunAsync("/bin/rm", "-rf", target);
        await RunAsync("/usr/bin/ditto", bundle, target);
        await RunAsync("/usr/bin/xattr", "-dr", "com.apple.quarantine", target);
        Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-n", target }, UseShellExecute = false });
    }

    private static async Task RunAsync(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(file)} failed: {error.Trim()}");
    }
}
