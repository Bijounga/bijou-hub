using System.Windows;
using System.Windows.Input;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Views;

namespace BijouHub;

// The focus screen: everything blacked out, just the timer in the middle with the plan in small type
// under it, for leaving on a second monitor. It opens on a monitor other than the one BijouHub is
// on (if there is one). And F11 takes the main window full screen.
public partial class MainWindow
{
    private BlackoutWindow? _blackout;
    private int _blackoutScreen;

    private void Blackout_Click(object sender, RoutedEventArgs e) => ToggleBlackout();

    private void ToggleBlackout()
    {
        if (_blackout != null)
        {
            _blackout.Close();
            return;
        }
        if (!IsSessionActive) return;

        var window = new BlackoutWindow();
        window.ExitRequested += () => window.Close();
        window.PauseRequested += TogglePause;
        window.FinishRequested += () => FinishSession_Click(this, new RoutedEventArgs());
        window.ScreenChosen += index =>
        {
            PlaceBlackout(index);
            RememberBlackoutScreen();
        };
        window.TaskPicked += SessionTask_Picked;
        window.TaskAdded += SessionTask_Added;
        window.TaskDone += SessionTask_Done;
        window.LookChanged += look =>
        {
            var settings = _settingsStore.Load();
            settings.FocusLook = look;
            _settingsStore.Save(settings);
        };
        window.Closed += (_, _) =>
        {
            _blackout = null;
            BlackoutButton.ToolTip = "Black out the screen and show just the timer";
        };

        // The monitor it was last put on, if that's still connected; otherwise another monitor than the
        // one BijouHub is on, if there is one: that's the one to leave it on.
        var screens = DisplayInfo.All();
        var mine = IsVisible ? DisplayInfo.Of(this) : null;
        var remembered = screens.FindIndex(s => ScreenKey(s) == _settingsStore.Load().FocusScreen);
        var start = remembered >= 0 ? remembered : screens.FindIndex(s => mine == null || s != mine);
        _blackoutScreen = start < 0 ? 0 : start;

        _blackout = window;
        BlackoutButton.ToolTip = "Leave the focus screen";
        window.SetLook(_settingsStore.Load().FocusLook);
        window.Show();
        PlaceBlackout(_blackoutScreen);
        UpdateBlackout();
        window.SetTasks(OpenTasksToday(), _activeTask);
    }

    private static string ScreenKey(DisplayInfo.Screen s) => $"{s.X},{s.Y},{s.Width}x{s.Height}";

    // The monitor picked with the buttons on the focus screen is the one it opens on next time.
    private void RememberBlackoutScreen()
    {
        var screens = DisplayInfo.All();
        if (_blackoutScreen < 0 || _blackoutScreen >= screens.Count) return;
        var settings = _settingsStore.Load();
        settings.FocusScreen = ScreenKey(screens[_blackoutScreen]);
        _settingsStore.Save(settings);
    }

    private void PlaceBlackout(int index)
    {
        if (_blackout is not { } window) return;
        var screens = DisplayInfo.All();
        if (screens.Count == 0) return;
        _blackoutScreen = (index % screens.Count + screens.Count) % screens.Count;
        DisplayInfo.Cover(window, screens[_blackoutScreen], topmost: true);

        var mine = IsVisible ? DisplayInfo.Of(this) : null;
        window.SetScreens(screens.Select((s, i) =>
            $"Screen {i + 1}: {s.Width}×{s.Height}" + (s.Primary ? ", main" : "") + (mine == s ? " — BijouHub is on this one" : "")).ToList(), _blackoutScreen);
    }

    // The plan under the timer: the task picked for this session, or else the project's next goal.
    private string? BlackoutGoalText()
    {
        if (_activeTask != null) return _activeTask.Text;
        var next = _activeProject?.Goals.FirstOrDefault(g => !g.IsComplete);
        return next?.Name;
    }

    private void UpdateBlackout()
    {
        if (_blackout == null) return;
        if (!IsSessionActive)
        {
            _blackout.Close();
            return;
        }
        _blackout.Update(TimerDisplay.Text, ActiveStatus.Text, _paused, BlackoutGoalText());
    }

    // ---------- F11: full screen ----------

    private bool _fullscreen;
    private DisplayInfo.Saved? _restoreSpot;

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            var screen = DisplayInfo.Of(this);
            _restoreSpot = DisplayInfo.Save(this);
            if (screen == null || _restoreSpot == null) return;
            _fullscreen = true;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            DisplayInfo.Cover(this, screen.Value, topmost: false);
        }
        else
        {
            _fullscreen = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            DarkTitleBar.Apply(this);
            if (_restoreSpot is { } spot) DisplayInfo.Restore(this, spot);
        }
    }
}
