using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BijouHub.Services;

// Local link for the BijouHub Stream Deck plugin (StreamDeck/ in this repo). Newline-delimited
// JSON over a loopback TCP socket: the plugin sends commands (catalog/start/pause/finish) and
// BijouHub pushes session state every tick so keys can draw a live countdown. Bound to
// 127.0.0.1 only, so nothing off this machine can reach it.
//
// bridge.json in the local data folder records the port and this exe's path, so the plugin
// can find BijouHub — and launch it when a key is pressed while it isn't running.
public sealed class StreamDeckBridge : IDisposable
{
    public const int DefaultPort = 47823;

    // Runs a command on the app's UI thread (WPF's Dispatcher, Avalonia's Dispatcher.UIThread).
    private readonly Func<Func<JsonObject?>, Task<JsonObject?>> _runOnUi;
    private readonly Func<JsonObject, JsonObject?> _handler;
    private readonly List<Client> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;

    public StreamDeckBridge(Func<Func<JsonObject?>, Task<JsonObject?>> runOnUi, Func<JsonObject, JsonObject?> handler)
    {
        _runOnUi = runOnUi;
        _handler = handler;
    }

    public int ClientCount
    {
        get { lock (_clients) return _clients.Count; }
    }

    public static int Port =>
        int.TryParse(Environment.GetEnvironmentVariable("BIJOUHUB_DECK_PORT"), out var port) ? port : DefaultPort;

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
        }
        catch (SocketException)
        {
            // Port taken (most likely a second BijouHub instance) — that one serves the deck.
            _listener = null;
            return;
        }

        WriteBridgeInfo();
        _ = AcceptLoopAsync(_listener, _cts.Token);
    }

    private static void WriteBridgeInfo()
    {
        var info = new JsonObject
        {
            ["port"] = Port,
            ["exePath"] = Environment.ProcessPath,
            ["pid"] = Environment.ProcessId
        };
        try { AtomicFile.WriteAllText(Path.Combine(DataPaths.LocalDir, "bridge.json"), info.ToJsonString()); }
        catch { /* the plugin falls back to the default port; only auto-launch is lost */ }
    }

    public void Broadcast(JsonObject message)
    {
        Client[] clients;
        lock (_clients) clients = _clients.ToArray();
        if (clients.Length == 0) return;

        var line = message.ToJsonString();
        foreach (var client in clients)
            _ = SendAsync(client, line);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await listener.AcceptTcpClientAsync(token); }
            catch { return; }

            var client = new Client(tcp);
            lock (_clients) _clients.Add(client);
            _ = ReadLoopAsync(client, token);
        }
    }

    private async Task ReadLoopAsync(Client client, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(client.Stream, new UTF8Encoding(false));
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line == null) break;
                if (line.Length == 0 || line.Length > 64 * 1024) continue;

                JsonObject? request;
                try { request = JsonNode.Parse(line) as JsonObject; }
                catch (JsonException) { continue; }
                if (request == null) continue;

                // Commands touch session state, so they run on the UI thread like a click would.
                var reply = await _runOnUi(() => _handler(request));
                if (reply != null && request["id"] is JsonNode id)
                {
                    reply["type"] = "reply";
                    reply["id"] = id.DeepClone();
                    await SendAsync(client, reply.ToJsonString());
                }
            }
        }
        catch
        {
            // Connection dropped — the plugin reconnects on its own.
        }
        finally
        {
            Drop(client);
        }
    }

    private async Task SendAsync(Client client, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await client.WriteLock.WaitAsync();
        try { await client.Stream.WriteAsync(bytes); }
        catch { Drop(client); }
        finally { client.WriteLock.Release(); }
    }

    private void Drop(Client client)
    {
        lock (_clients) _clients.Remove(client);
        client.Tcp.Dispose();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        lock (_clients)
        {
            foreach (var client in _clients) client.Tcp.Dispose();
            _clients.Clear();
        }
    }

    private sealed class Client(TcpClient tcp)
    {
        public TcpClient Tcp { get; } = tcp;
        public NetworkStream Stream { get; } = tcp.GetStream();
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }
}
