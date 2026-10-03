using System.Net.Http;
using System.Net.Http.Headers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using BijouHub.Mac.Services;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

// Connection status, plus a one-click plugin install: the plugin file is downloaded from the
// latest BijouHub release and handed to the Stream Deck app, which shows its own install prompt.
public partial class StreamDeckWindow : Window
{
    private const string PluginUuid = StreamDeckPlugin.Uuid;
    private const string PluginUrl = "https://github.com/Bijounga/bijou-hub/releases/latest/download/com.bijounga.bijouhub.streamDeckPlugin";

    private readonly Func<int> _connected;
    private readonly DispatcherTimer _timer;

    // The latest release's plugin, fetched once per run: its version decides whether to offer an
    // update, and Install reuses the bytes.
    private static byte[]? _latestPackage;
    private static Version? _latestVersion;

    public StreamDeckWindow() : this(() => 0) { }

    public StreamDeckWindow(Func<int> connectedPlugins)
    {
        InitializeComponent();
        _connected = connectedPlugins;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        RefreshStatus();
        if (_latestPackage == null) _ = FetchLatestAsync();
    }

    private static async Task<byte[]> DownloadPackageAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BijouHub", MacUpdateService.GetCurrentVersion()));
        var bytes = await http.GetByteArrayAsync(PluginUrl);
        _latestPackage = bytes;
        using var stream = new MemoryStream(bytes);
        _latestVersion = StreamDeckPlugin.PackageVersion(stream);
        return bytes;
    }

    private async Task FetchLatestAsync()
    {
        try
        {
            await DownloadPackageAsync();
            RefreshStatus();
        }
        catch
        {
            // offline: Install downloads it on demand
        }
    }

    private static bool PluginInstalled => StreamDeckPlugin.Installed;

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private void RefreshStatus()
    {
        var outdated = StreamDeckPlugin.UpdateAvailable(_latestVersion);
        var (text, brush) = outdated
            ? ($"Plugin {_latestVersion!.ToString(2)} is here with new keys. Update to get them.", "AccentBrush")
            : _connected() > 0
            ? ("Connected — your Stream Deck is ready.", "SuccessBrush")
            : PluginInstalled
                ? ("Plugin installed — waiting for Stream Deck to connect. Is the Stream Deck app running?", "HazardBrush")
                : ("Plugin not installed yet.", "FaintTextBrush");
        StatusText.Text = text;
        StatusDot.Fill = Brush(brush);
        InstallButton.Content = outdated ? "Update Plugin" : PluginInstalled ? "Reinstall Plugin" : "Install Plugin";
    }

    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        InstallButton.Content = "Downloading…";
        try
        {
            var bytes = _latestPackage ?? await DownloadPackageAsync();
            var path = Path.Combine(Path.GetTempPath(), PluginUuid + ".streamDeckPlugin");
            await File.WriteAllBytesAsync(path, bytes);
            MacPlatform.Open(path);
        }
        catch (Exception ex)
        {
            await PromptWindow.Notice(this, "Stream Deck", $"Couldn't get the plugin: {ex.Message}\n\nIs the Stream Deck app installed?");
        }
        finally
        {
            InstallButton.IsEnabled = true;
            RefreshStatus();
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
