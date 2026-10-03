using System.Windows;
using System.Windows.Controls;
using BijouHub.Services;

namespace BijouHub;

// "Keep running in the background": closing the window hides BijouHub to the notification area
// (the hidden icons, like Discord) so Stream Deck keys keep working. Click the icon to bring it
// back; right-click for Quick Capture, the running session and Quit.
public partial class MainWindow
{
    private bool _keepRunning;
    private TrayIcon? _tray;

    private void InitTray()
    {
        _keepRunning = _settingsStore.Load().KeepRunningWhenClosed;
        UpdateKeepRunningButton();
        if (_keepRunning) ShowTrayIcon();
    }

    private void ShowTrayIcon()
    {
        if (_tray == null)
        {
            _tray = new TrayIcon();
            _tray.Clicked += BringToFront;
            _tray.MenuRequested += OpenTrayMenu;
        }
        _tray.Visible = true;
        UpdateTrayToolTip();
    }

    // Closing with the option on: out of the taskbar and into the tray.
    private void HideToTray()
    {
        ShowTrayIcon();
        Hide();

        var settings = _settingsStore.Load();
        if (settings.TrayHintShown) return;
        settings.TrayHintShown = true;
        _settingsStore.Save(settings);
        _tray!.ShowBalloon("BijouHub is still running", "It's in the hidden icons, so your Stream Deck keys keep working. Right-click it to quit.");
    }

    private void KeepRunning_Click(object sender, RoutedEventArgs e)
    {
        _keepRunning = !_keepRunning;
        var settings = _settingsStore.Load();
        settings.KeepRunningWhenClosed = _keepRunning;
        _settingsStore.Save(settings);
        UpdateKeepRunningButton();
        if (_keepRunning) ShowTrayIcon();
        else if (_tray != null) _tray.Visible = false;
    }

    private void UpdateKeepRunningButton()
    {
        KeepRunningButton.ToolTip = _keepRunning
            ? "Closing hides BijouHub in the hidden icons, so the Stream Deck keeps working. Click to turn off; right-click to quit."
            : "Keep running in the background when closed (for the Stream Deck)";
        SetToolActive(KeepRunningButton, _keepRunning);
    }

    // The hover text shows the running session, so a glance at the tray tells you the time.
    private void UpdateTrayToolTip()
    {
        if (_tray is not { Visible: true }) return;
        _tray.ToolTip = IsSessionActive
            ? $"BijouHub · {_activeProject?.Name ?? _activeMode?.Name} · {(_paused ? "paused" : TimerDisplay.Text)}"
            : "BijouHub";
    }

    private void OpenTrayMenu()
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Open BijouHub", BringToFront);
        Add("Quick capture", ShowQuickCapture);
        if (IsSessionActive)
        {
            menu.Items.Add(new Separator());
            Add(_paused ? "Resume" : "Pause", TogglePause);
            Add("Finish session", () => FinishSession(askForNote: false));
        }
        menu.Items.Add(new Separator());
        Add("Quit BijouHub", QuitApp);

        _tray!.PrepareForMenu();
        menu.IsOpen = true;
    }

    // Launched at sign-in: no window, just the tray icon.
    public void StartInTray() => ShowTrayIcon();

    private void Quit_Click(object sender, RoutedEventArgs e) => QuitApp();

    private void QuitApp()
    {
        App.Quitting = true;
        Close();
    }
}
