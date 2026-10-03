using System.Diagnostics;
using System.IO;
using System.Windows;
using Shape = System.Windows.Shapes.Shape;
using System.Windows.Threading;
using BijouHub.Services;

namespace BijouHub.Views;

public partial class StreamDeckWindow : Window
{
    private const string PluginUuid = StreamDeckPlugin.Uuid;

    // The plugin version packed into this exe, read once.
    private static readonly Lazy<Version?> BundledVersion = new(() =>
    {
        using var resource = typeof(StreamDeckWindow).Assembly.GetManifestResourceStream("BijouHub.StreamDeckPlugin");
        return resource == null ? null : StreamDeckPlugin.PackageVersion(resource);
    });

    private readonly Func<int> _connectedPlugins;
    private readonly DispatcherTimer _statusTimer;

    public StreamDeckWindow(Func<int> connectedPlugins)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _connectedPlugins = connectedPlugins;

        // Polled so the status flips to "Connected" on its own right after an install.
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        Closed += (_, _) => _statusTimer.Stop();
        RefreshStatus();
    }

    private static bool PluginInstalled => StreamDeckPlugin.Installed;

    private void RefreshStatus()
    {
        string text, brushKey;
        var outdated = StreamDeckPlugin.UpdateAvailable(BundledVersion.Value);
        if (outdated)
        {
            text = $"Plugin {BundledVersion.Value!.ToString(2)} is here with new keys. Update to get them.";
            brushKey = "AccentBrush";
        }
        else if (_connectedPlugins() > 0)
        {
            text = "Connected — your Stream Deck is ready.";
            brushKey = "SuccessBrush";
        }
        else if (PluginInstalled)
        {
            text = "Plugin installed — waiting for Stream Deck to connect. Is the Stream Deck app running?";
            brushKey = "HazardBrush";
        }
        else
        {
            text = "Plugin not installed yet.";
            brushKey = "FaintTextBrush";
        }

        StatusText.Text = text;
        StatusDot.SetResourceReference(Shape.FillProperty, brushKey);
        InstallButton.Content = outdated ? "Update Plugin" : PluginInstalled ? "Reinstall Plugin" : "Install Plugin";
    }

    // The packed plugin ships inside the exe; opening the file hands it to the Stream Deck app,
    // which shows its own install prompt.
    private void Install_Click(object sender, RoutedEventArgs e)
    {
        using var resource = typeof(StreamDeckWindow).Assembly.GetManifestResourceStream("BijouHub.StreamDeckPlugin");
        if (resource == null)
        {
            MessageBox.Show("This build doesn't include the Stream Deck plugin. Download it from the BijouHub release page.",
                "Stream Deck", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var path = Path.Combine(Path.GetTempPath(), PluginUuid + ".streamDeckPlugin");
            using (var file = File.Create(path))
                resource.CopyTo(file);

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open the plugin installer: {ex.Message}\n\nIs the Stream Deck app installed?",
                "Stream Deck", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
