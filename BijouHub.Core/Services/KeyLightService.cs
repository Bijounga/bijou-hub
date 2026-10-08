using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace BijouHub.Services;

// Elgato Key Light (Air) over its local HTTP API (port 9123): find it on the network, switch it
// on and off. Nothing leaves the local network, and a light that's missing or asleep is never
// an error for the caller: every call just reports whether it worked.
public static class KeyLightService
{
    public const int DefaultPort = 9123;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static bool _turnedOnByUs;

    // "192.168.1.40", "192.168.1.40:9123", "http://key-light.local" → "http://host:port".
    public static string? BaseUrl(string? address)
    {
        var text = address?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return null;
        var port = uri.IsDefaultPort ? DefaultPort : uri.Port;
        return $"http://{(uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.Host}]" : uri.Host)}:{port}";
    }

    // The light's name ("Elgato Key Light Air 1234"), or null when nothing answers there.
    public static async Task<string?> ProbeAsync(string? address, int timeoutMs = 3000)
    {
        if (BaseUrl(address) is not { } baseUrl) return null;
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var json = await Http.GetStringAsync(baseUrl + "/elgato/accessory-info", cts.Token);
            return Field(json, "displayName") is { Length: > 0 } name ? name : Field(json, "productName") ?? "Elgato light";
        }
        catch
        {
            return null;
        }
    }

    // Pulls "name":"value" out of the small JSON reply without a model for it.
    private static string? Field(string json, string name)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> SetPowerAsync(string? address, bool on)
    {
        if (BaseUrl(address) is not { } baseUrl) return false;
        try
        {
            using var content = new StringContent($"{{\"numberOfLights\":1,\"lights\":[{{\"on\":{(on ? 1 : 0)}}}]}}", Encoding.UTF8, "application/json");
            using var response = await Http.PutAsync(baseUrl + "/elgato/lights", content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // A session in a Key Light mode starts: light up. Remembers it was us, so ending a session
    // never turns off a light the user switched on themselves.
    public static async Task SessionStartedAsync(string? address)
    {
        _turnedOnByUs = await SetPowerAsync(address, true);
    }

    // Safe to call when no session lit the light; a blocking wait is for app exit, where a
    // fire-and-forget request would be cut off.
    public static void SessionEnded(string? address, bool offWhenDone, bool wait = false)
    {
        if (!_turnedOnByUs) return;
        _turnedOnByUs = false;
        if (!offWhenDone) return;
        var task = Task.Run(() => SetPowerAsync(address, false));
        if (wait) task.Wait(TimeSpan.FromSeconds(2));
    }

    // Looks for lights on this computer's own networks: every address in each /24 is asked for
    // the light's name, a few dozen at a time.
    public static async Task<List<(string Address, string Name)>> ScanAsync(CancellationToken cancel = default)
    {
        var hosts = new HashSet<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var bytes = unicast.Address.GetAddressBytes();
                for (var last = 1; last < 255; last++) hosts.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}.{last}");
            }
        }

        var found = new List<(string, string)>();
        using var gate = new SemaphoreSlim(48);
        var probes = hosts.Select(async host =>
        {
            await gate.WaitAsync(cancel);
            try
            {
                if (await ProbeAsync(host, 700) is { } name)
                    lock (found) found.Add((host, name));
            }
            finally
            {
                gate.Release();
            }
        });
        try { await Task.WhenAll(probes); }
        catch (OperationCanceledException) { }
        return found.OrderBy(f => f.Item1, StringComparer.Ordinal).ToList();
    }
}
