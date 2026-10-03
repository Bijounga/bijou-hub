using System.Net.Http;
using System.Net.Http.Headers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using BijouHub.Mac.Services;

namespace BijouHub.Mac.Views;

// Connection status, plus a one-click plugin install: the plugin file is downloaded from the
// latest BijouHub release and handed to the Stream Deck app, which shows its own install prompt.
public partial class StreamDeckWindow : Window
{
    private const string PluginUuid = "com.bijounga.bijouhub";
    private const string PluginUrl = "https://github.com/Bijounga/bijou-hub/releases/latest/download/com.bijounga.bijouhub.streamDeckPlugin";

    private readonly Func<int> _connected;
    private readonly DispatcherTimer _timer;

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
    }

    private static string PluginsDir => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "com.elgato.StreamDeck", "Plugins")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins");

    private static bool PluginInstalled => Directory.Exists(Path.Combine(PluginsDir, PluginUuid + ".sdPlugin"));

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private void RefreshStatus()
    {
        var (text, brush) = _connected() > 0
            ? ("Connected — your Stream Deck is ready.", "SuccessBrush")
            : PluginInstalled
                ? ("Plugin installed — waiting for Stream Deck to connect. Is the Stream Deck app running?", "HazardBrush")
                : ("Plugin not installed yet.", "FaintTextBrush");
        StatusText.Text = text;
        StatusDot.Fill = Brush(brush);
        InstallButton.Content = PluginInstalled ? "Reinstall Plugin" : "Install Plugin";
    }

    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        InstallButton.Content = "Downloading…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BijouHub", MacUpdateService.GetCurrentVersion()));
            var bytes = await http.GetByteArrayAsync(PluginUrl);
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
