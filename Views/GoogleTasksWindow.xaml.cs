using System.IO;
using System.Windows;
using BijouHub.Services;
using BijouHub.Services.GoogleTasks;
using Microsoft.Win32;
using Shape = System.Windows.Shapes.Shape;

namespace BijouHub.Views;

public partial class GoogleTasksWindow : Window
{
    private readonly GoogleAuth _auth;
    private CancellationTokenSource? _signIn;

    public GoogleTasksWindow(GoogleAuth auth)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _auth = auth;
        Closed += (_, _) => _signIn?.Cancel();
        Refresh();
    }

    private void Refresh(string? message = null, bool problem = false)
    {
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
            text = "Not set up yet — goals are only on this computer.";
            brush = "FaintTextBrush";
        }

        StatusText.Text = text;
        StatusDot.SetResourceReference(Shape.FillProperty, brush);

        var signingIn = _signIn != null;
        ImportButton.Content = _auth.HasClient ? "Replace Client JSON…" : "Import Client JSON…";
        ImportButton.IsEnabled = !signingIn;
        SignInButton.IsEnabled = _auth.HasClient && !signingIn;
        SignInButton.Content = _auth.IsSignedIn ? "Sign in again" : "Sign in with Google";
        CancelSignInButton.Visibility = signingIn ? Visibility.Visible : Visibility.Collapsed;
        DisconnectButton.Visibility = _auth.IsSignedIn && !signingIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the client JSON downloaded from Google Cloud",
            Filter = "Google client JSON (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is var home &&
                               Directory.Exists(Path.Combine(home, "Downloads")) ? Path.Combine(home, "Downloads") : null
        };
        if (dialog.ShowDialog(this) != true) return;

        GoogleAuth.ClientConfig? client = null;
        try { client = GoogleAuth.ParseClient(File.ReadAllText(dialog.FileName)); }
        catch { /* reported below */ }

        if (client == null)
        {
            Refresh("That file doesn't look like a Google OAuth client JSON (it needs a client_id and client_secret).", problem: true);
            return;
        }

        _auth.SaveClient(client);
        Refresh("Client imported. Now sign in.");
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
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

    private void CancelSignIn_Click(object sender, RoutedEventArgs e) => _signIn?.Cancel();

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "Stop syncing with Google Tasks? Your tasks stay in Google; BijouHub just stops reading and writing them.",
            "Disconnect Google Tasks", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        await _auth.SignOutAsync();
        Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
