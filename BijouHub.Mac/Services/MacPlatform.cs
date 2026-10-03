using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BijouHub.Models;

namespace BijouHub.Mac.Services;

// The handful of things that differ by OS. The app ships for macOS; the Windows branches let it
// run on a Windows dev machine for testing.
public static class MacPlatform
{
    // ---------- Idle time (pauses the timer when nobody's at the keyboard) ----------

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static TimeSpan IdleTime()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                return TimeSpan.FromSeconds(CGEventSourceSecondsSinceLastEventType(0 /* combined session */, uint.MaxValue /* any input */));
            if (OperatingSystem.IsWindows())
            {
                var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
                if (GetLastInputInfo(ref info)) return TimeSpan.FromMilliseconds(Environment.TickCount64 - info.dwTime);
            }
        }
        catch
        {
            // No idle source — treat as active.
        }
        return TimeSpan.Zero;
    }

    // ---------- Opening things ----------

    public static void Open(string target, string? withApp = null)
    {
        if (OperatingSystem.IsMacOS())
        {
            var start = new ProcessStartInfo("open") { UseShellExecute = false };
            if (!string.IsNullOrEmpty(withApp))
            {
                start.ArgumentList.Add("-a");
                start.ArgumentList.Add(withApp);
            }
            start.ArgumentList.Add(target);
            Process.Start(start);
        }
        else
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
    }

    // Opens a mode's launch items in order, honouring each item's delay. Returns what failed.
    public static async Task<List<string>> LaunchAsync(WorkMode mode)
    {
        var failures = new List<string>();
        foreach (var item in mode.LaunchItems)
        {
            if (item.DelayMs > 0) await Task.Delay(item.DelayMs);
            try
            {
                switch (item.Type)
                {
                    case LaunchItemType.Url:
                    case LaunchItemType.BrowserUrl:
                        Open(item.Path, OperatingSystem.IsMacOS() ? BrowserApp(item.Browser) : null);
                        break;
                    case LaunchItemType.Application:
                        Open(item.Path);
                        break;
                    default:
                        Open(item.Path, string.IsNullOrEmpty(item.OpenWithAppPath) ? null : item.OpenWithAppPath);
                        break;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{item.DisplayName}: {ex.Message}");
            }
        }
        return failures;
    }

    private static string? BrowserApp(string? browser) => browser?.ToLowerInvariant() switch
    {
        "chrome" => "Google Chrome",
        "edge" => "Microsoft Edge",
        "firefox" => "Firefox",
        "brave" => "Brave Browser",
        "safari" => "Safari",
        _ => null
    };

    // ---------- Start at login (a per-user LaunchAgent, like any Mac app's login item) ----------

    private static string LaunchAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", "com.bijounga.bijouhub.plist");

    public static bool StartsAtLogin => OperatingSystem.IsMacOS() && File.Exists(LaunchAgentPath);

    // Launched at login: start in the menu bar when "keep running in the background" is on.
    public const string TrayArgument = "--tray";

    // Login items from older versions open the app without the argument; rewrite them once.
    public static void UpgradeLoginItem()
    {
        try
        {
            if (StartsAtLogin && !File.ReadAllText(LaunchAgentPath).Contains(TrayArgument)) SetStartsAtLogin(true);
        }
        catch
        {
            // leave it as it was
        }
    }

    public static void SetStartsAtLogin(bool on)
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (!on)
        {
            File.Delete(LaunchAgentPath);
            return;
        }

        var bundle = MacUpdateService.BundlePath ?? "/Applications/BijouHub.app";
        Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentPath)!);
        File.WriteAllText(LaunchAgentPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key><string>com.bijounga.bijouhub</string>
                <key>ProgramArguments</key><array><string>/usr/bin/open</string><string>-a</string><string>{System.Security.SecurityElement.Escape(bundle)}</string><string>--args</string><string>{TrayArgument}</string></array>
                <key>RunAtLoad</key><true/>
            </dict>
            </plist>
            """);
    }

    // ---------- Protecting the Google sign-in token ----------
    // The refresh token is AES-GCM encrypted on disk with a key that lives in the login Keychain
    // (created on first use). Windows uses DPAPI for the same job.

    private const string KeychainService = "BijouHub";
    private const string KeychainAccount = "google-token-key";

    public static byte[] Protect(byte[] plain)
    {
        var key = TokenKey(create: true);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        return nonce.Concat(tag).Concat(cipher).ToArray();
    }

    public static byte[] Unprotect(byte[] sealedBytes)
    {
        var key = TokenKey(create: false);
        var nonce = sealedBytes[..12];
        var tag = sealedBytes[12..28];
        var cipher = sealedBytes[28..];
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private static byte[] TokenKey(bool create)
    {
        if (!OperatingSystem.IsMacOS())
            return SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + Environment.MachineName + KeychainAccount)); // dev runs only

        var existing = RunSecurity("find-generic-password", "-s", KeychainService, "-a", KeychainAccount, "-w");
        if (existing != null) return Convert.FromBase64String(existing.Trim());
        if (!create) throw new CryptographicException("No token key in the Keychain");

        var key = RandomNumberGenerator.GetBytes(32);
        RunSecurity("add-generic-password", "-s", KeychainService, "-a", KeychainAccount, "-w", Convert.ToBase64String(key), "-U");
        return key;
    }

    private static string? RunSecurity(params string[] args)
    {
        var start = new ProcessStartInfo("/usr/bin/security") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10000);
        return process.ExitCode == 0 ? output : null;
    }
}
