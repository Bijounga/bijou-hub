using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform;

namespace BijouHub.Mac;

// "Keep running in the background": closing the window hides BijouHub to the menu bar (the
// Mac's equivalent of the hidden icons) so Stream Deck keys keep working. Click it, or the
// Dock icon, to bring the window back; its menu has Quick Capture, the session and Quit.
public partial class MainWindow
{
    private bool _keepRunning;
    private bool _quitting;
    private TrayIcon? _tray;

    private void InitTray()
    {
        _keepRunning = _settingsStore.Load().KeepRunningWhenClosed;
        if (_keepRunning) ShowTrayIcon();

        // Clicking BijouHub in the Dock while its window is hidden opens it again.
        if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } activatable)
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen) BringToFront();
            };
    }

    private void ShowTrayIcon()
    {
        if (_tray == null)
        {
            _tray = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://BijouHub.Mac/Assets/tray.png"))),
                ToolTipText = "BijouHub",
                Menu = new NativeMenu()
            };
            MacOSProperties.SetIsTemplateIcon(_tray, true);
            _tray.Clicked += (_, _) => BringToFront();
            _tray.Menu.Opening += (_, _) => FillTrayMenu(_tray.Menu);
            FillTrayMenu(_tray.Menu);
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
        }
        _tray.IsVisible = true;
    }

    private void HideToTray()
    {
        ShowTrayIcon();
        Hide();
    }

    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    // Rebuilt each time it opens, so the session items match what's running.
    private void FillTrayMenu(NativeMenu menu)
    {
        menu.Items.Clear();
        void Add(string header, Action action)
        {
            var item = new NativeMenuItem(header);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Open BijouHub", BringToFront);
        Add("Quick Capture", ShowQuickCapture);
        if (IsSessionActive)
        {
            menu.Items.Add(new NativeMenuItemSeparator());
            Add(_paused ? "Resume" : "Pause", TogglePause);
            Add("Finish Session", () => _ = FinishSessionAsync(askForNote: false));
        }
        menu.Items.Add(new NativeMenuItemSeparator());
        Add("Quit BijouHub", QuitApp);
    }

    private void UpdateTrayToolTip()
    {
        if (_tray is not { IsVisible: true }) return;
        _tray.ToolTipText = IsSessionActive
            ? $"BijouHub · {_activeProject?.Name ?? _activeMode?.Name} · {(_paused ? "paused" : TimerDisplay)}"
            : "BijouHub";
    }

    private void KeepRunning_Click(object? sender, RoutedEventArgs e)
    {
        _keepRunning = !_keepRunning;
        var settings = _settingsStore.Load();
        settings.KeepRunningWhenClosed = _keepRunning;
        _settingsStore.Save(settings);
        RefreshToolStates();
        if (_keepRunning) ShowTrayIcon();
        else if (_tray != null) _tray.IsVisible = false;
    }

    private void Quit_Click(object? sender, RoutedEventArgs e) => QuitApp();

    private void QuitApp()
    {
        _quitting = true;
        if (_tray != null) _tray.IsVisible = false;
        Close();
    }
}
