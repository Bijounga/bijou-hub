using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BijouHub.Services.GoogleTasks;

namespace BijouHub.Mac.Views;

public partial class GoogleTasksWindow : Window
{
    private readonly GoogleAuth? _auth;
    private CancellationTokenSource? _signIn;

    public GoogleTasksWindow() => InitializeComponent();

    public GoogleTasksWindow(GoogleAuth auth)
    {
        InitializeComponent();
        _auth = auth;
        Closed += (_, _) => _signIn?.Cancel();
        Refresh();
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private void Refresh(string? message = null, bool problem = false)
    {
        if (_auth == null) return;
        string text, brush;
        if (message != null)
        {
            text = message;
            brush = problem ? "HazardBrush" : "AccentBrush";
        }
        else if (_auth.IsSignedIn)
        {
            text = "Connected — goals sync with Google Tasks.";
            brush = "SuccessBrush";
        }
        else if (_auth.HasClient)
        {
            text = "Client imported. Sign in to start syncing.";
            brush = "HazardBrush";
        }
        else
        {
            text = "Not set up yet — goals are only on this Mac.";
            brush = "FaintTextBrush";
        }
        StatusText.Text = text;
        StatusDot.Fill = Brush(brush);

        var signingIn = _signIn != null;
        ImportButton.Content = _auth.HasClient ? "Replace Client JSON…" : "Import Client JSON…";
        ImportButton.IsEnabled = !signingIn;
        SignInButton.IsEnabled = _auth.HasClient && !signingIn;
        SignInButton.Content = _auth.IsSignedIn ? "Sign in again" : "Sign in with Google";
        CancelSignInButton.IsVisible = signingIn;
        DisconnectButton.IsVisible = _auth.IsSignedIn && !signingIn;
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (_auth == null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the client JSON downloaded from Google Cloud",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Google client JSON") { Patterns = new[] { "*.json" } } }
        });
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path == null) return;

        GoogleAuth.ClientConfig? client = null;
        try { client = GoogleAuth.ParseClient(await File.ReadAllTextAsync(path)); }
        catch { /* reported below */ }
        if (client == null)
        {
            Refresh("That file doesn't look like a Google OAuth client JSON (it needs a client_id and client_secret).", problem: true);
            return;
        }
        _auth.SaveClient(client);
        Refresh("Client imported. Now sign in.");
    }

    private async void SignIn_Click(object? sender, RoutedEventArgs e)
    {
        if (_auth == null) return;
        _signIn = new CancellationTokenSource();
        Refresh("Waiting for you to approve in the browser…");
        try
        {
            await _auth.SignInAsync(_signIn.Token);
            _signIn = null;
            Refresh();
        }
        catch (OperationCanceledException)
        {
            _signIn = null;
            Refresh("Sign-in was cancelled or timed out.", problem: true);
        }
        catch (Exception ex)
        {
            _signIn = null;
            Refresh(ex.Message, problem: true);
        }
    }

    private void CancelSignIn_Click(object? sender, RoutedEventArgs e) => _signIn?.Cancel();

    private async void Disconnect_Click(object? sender, RoutedEventArgs e)
    {
        if (_auth == null) return;
        if (!await PromptWindow.Confirm(this, "Disconnect Google Tasks", "Stop syncing with Google Tasks? Your tasks stay in Google; BijouHub just stops reading and writing them.", "Disconnect"))
            return;
        await _auth.SignOutAsync();
        Refresh();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
