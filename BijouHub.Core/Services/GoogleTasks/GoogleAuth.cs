using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BijouHub.Services.GoogleTasks;

// Google sign-in for an installed app: the browser opens Google's consent page, Google redirects
// back to a one-shot listener on 127.0.0.1, and the code is exchanged (with PKCE) for tokens.
// No server of our own is involved.
//
// The OAuth client (a "Desktop app" client from the user's own Google Cloud project) is read
// from the client JSON Google lets you download, copied to google-client.json. The refresh
// token is kept in google-token.dat, encrypted by the caller-supplied protector (DPAPI on Windows).
public sealed class GoogleAuth
{
    public const string TasksScope = "https://www.googleapis.com/auth/tasks";

    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    private readonly string _clientPath = Path.Combine(DataPaths.LocalDir, "google-client.json");
    private readonly string _tokenPath = Path.Combine(DataPaths.LocalDir, "google-token.dat");
    private readonly Func<byte[], byte[]> _protect;
    private readonly Func<byte[], byte[]> _unprotect;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _accessToken;
    private DateTime _accessTokenExpires;

    public GoogleAuth(HttpClient http, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect)
    {
        _http = http;
        _protect = protect;
        _unprotect = unprotect;
    }

    public sealed record ClientConfig(string ClientId, string ClientSecret);

    public bool HasClient => LoadClient() != null;

    public bool IsSignedIn => File.Exists(_tokenPath) && LoadClient() != null;

    // Tests point BijouHub at a fake Tasks server with a fixed token, skipping Google entirely.
    private static string? TestToken => Environment.GetEnvironmentVariable("BIJOUHUB_GTASKS_TEST_TOKEN");

    public bool IsReady => TestToken != null || IsSignedIn;

    public ClientConfig? LoadClient()
    {
        if (TestToken != null) return new ClientConfig("test", "test");
        try
        {
            return File.Exists(_clientPath) ? ParseClient(File.ReadAllText(_clientPath)) : null;
        }
        catch
        {
            return null;
        }
    }

    // Accepts the JSON Google offers to download for an OAuth client ("installed" for Desktop
    // clients, "web" if the user picked that type), or a flat {client_id, client_secret}.
    public static ClientConfig? ParseClient(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject;
        var section = root?["installed"] as JsonObject ?? root?["web"] as JsonObject ?? root;
        var id = (string?)section?["client_id"];
        var secret = (string?)section?["client_secret"];
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret) ? null : new ClientConfig(id, secret);
    }

    public void SaveClient(ClientConfig client)
    {
        var json = new JsonObject { ["client_id"] = client.ClientId, ["client_secret"] = client.ClientSecret };
        AtomicFile.WriteAllText(_clientPath, json.ToJsonString());
    }

    // Opens the browser for consent and waits (up to the token's lifetime of patience: 5 minutes)
    // for Google to redirect back. Throws with a readable message on denial or timeout.
    public async Task SignInAsync(CancellationToken cancel)
    {
        var client = LoadClient() ?? throw new InvalidOperationException("Import your Google client JSON first.");

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}";

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var url = AuthEndpoint +
                  "?response_type=code" +
                  $"&client_id={Uri.EscapeDataString(client.ClientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  $"&scope={Uri.EscapeDataString(TasksScope)}" +
                  $"&code_challenge={challenge}&code_challenge_method=S256" +
                  $"&state={state}" +
                  "&access_type=offline&prompt=consent";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        var query = await ReceiveRedirectAsync(listener, timeout.Token);
        if (query.TryGetValue("error", out var error))
            throw new InvalidOperationException(error == "access_denied" ? "Sign-in was cancelled." : $"Google said: {error}");
        if (!query.TryGetValue("state", out var returnedState) || returnedState != state)
            throw new InvalidOperationException("Sign-in response didn't match — please try again.");
        if (!query.TryGetValue("code", out var code))
            throw new InvalidOperationException("Google didn't return a sign-in code.");

        var tokens = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = client.ClientId,
            ["client_secret"] = client.ClientSecret
        }, cancel);

        var refresh = (string?)tokens["refresh_token"]
                      ?? throw new InvalidOperationException("Google didn't return a refresh token — try signing in again.");
        SaveRefreshToken(refresh);
        ApplyAccessToken(tokens);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancel)
    {
        if (TestToken is { } test) return test;
        if (_accessToken != null && DateTime.UtcNow < _accessTokenExpires) return _accessToken;

        await _refreshLock.WaitAsync(cancel);
        try
        {
            if (_accessToken != null && DateTime.UtcNow < _accessTokenExpires) return _accessToken;

            var client = LoadClient() ?? throw new GoogleSignInRequiredException();
            var refresh = LoadRefreshToken() ?? throw new GoogleSignInRequiredException();
            JsonObject tokens;
            try
            {
                tokens = await PostTokenAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refresh,
                    ["client_id"] = client.ClientId,
                    ["client_secret"] = client.ClientSecret
                }, cancel);
            }
            catch (GoogleTokenException ex) when (ex.Error is "invalid_grant" or "invalid_client" or "unauthorized_client")
            {
                // Revoked, expired (a "Testing" consent screen expires tokens after 7 days), or the client was deleted.
                SignOutLocally();
                throw new GoogleSignInRequiredException();
            }
            ApplyAccessToken(tokens);
            return _accessToken!;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void InvalidateAccessToken() => _accessToken = null;

    public async Task SignOutAsync()
    {
        var refresh = LoadRefreshToken();
        SignOutLocally();
        if (refresh == null) return;
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh });
            await _http.PostAsync(RevokeEndpoint, content);
        }
        catch
        {
            // Best effort — the local token is gone either way.
        }
    }

    private void SignOutLocally()
    {
        _accessToken = null;
        try { File.Delete(_tokenPath); } catch { /* already gone */ }
    }

    private void ApplyAccessToken(JsonObject tokens)
    {
        _accessToken = (string?)tokens["access_token"];
        var seconds = tokens["expires_in"] is JsonValue v && v.TryGetValue<int>(out var s) ? s : 3600;
        _accessTokenExpires = DateTime.UtcNow.AddSeconds(seconds - 60);
    }

    private async Task<JsonObject> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancel)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(TokenEndpoint, content, cancel);
        var body = await response.Content.ReadAsStringAsync(cancel);
        var json = JsonNode.Parse(body) as JsonObject ?? new JsonObject();
        if (!response.IsSuccessStatusCode)
            throw new GoogleTokenException((string?)json["error"] ?? response.StatusCode.ToString(), (string?)json["error_description"]);
        return json;
    }

    private void SaveRefreshToken(string token)
    {
        var bytes = _protect(Encoding.UTF8.GetBytes(token));
        var temp = _tokenPath + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, _tokenPath, overwrite: true);
    }

    private string? LoadRefreshToken()
    {
        try
        {
            return File.Exists(_tokenPath) ? Encoding.UTF8.GetString(_unprotect(File.ReadAllBytes(_tokenPath))) : null;
        }
        catch
        {
            return null; // unreadable (e.g. copied from another Windows account) — sign in again
        }
    }

    // Minimal one-request HTTP server: reads the redirect's query string, answers with a
    // "you can close this tab" page, and stops.
    private static async Task<Dictionary<string, string>> ReceiveRedirectAsync(TcpListener listener, CancellationToken cancel)
    {
        while (true)
        {
            using var tcp = await listener.AcceptTcpClientAsync(cancel);
            await using var stream = tcp.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancel) ?? "";
            var target = requestLine.Split(' ') is { Length: >= 2 } parts ? parts[1] : "/";

            var queryIndex = target.IndexOf('?');
            if (queryIndex < 0)
            {
                // A favicon or preflight request — not the redirect.
                await WriteResponseAsync(stream, 404, "Not found", cancel);
                continue;
            }

            var query = target[(queryIndex + 1)..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "");

            var ok = query.ContainsKey("code");
            await WriteResponseAsync(stream, 200,
                "<!doctype html><meta charset=utf-8><title>BijouHub</title>" +
                "<body style=\"font:16px Segoe UI,system-ui,sans-serif;background:#0b0d12;color:#e8ecf2;display:grid;place-items:center;height:90vh\">" +
                $"<div style=\"text-align:center\"><h2>{(ok ? "BijouHub is connected to Google Tasks" : "Sign-in didn't finish")}</h2>" +
                "<p style=\"color:#8a93a6\">You can close this tab and go back to BijouHub.</p></div>", cancel);
            return query;
        }
    }

    private static async Task WriteResponseAsync(Stream stream, int status, string body, CancellationToken cancel)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\n" +
                     $"Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancel);
        await stream.WriteAsync(bytes, cancel);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class GoogleSignInRequiredException() : Exception("Sign in to Google Tasks again.");

public sealed class GoogleTokenException(string error, string? description)
    : Exception(description ?? error)
{
    public string Error { get; } = error;
}
