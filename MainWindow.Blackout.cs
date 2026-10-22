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
        window.NextScreenRequested += () => PlaceBlackout(_blackoutScreen + 1);
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

        // Another monitor than the one BijouHub is on, if there is one: that's the one to leave it on.
        var screens = DisplayInfo.All();
        var mine = IsVisible ? DisplayInfo.Of(this) : null;
        var start = screens.FindIndex(s => mine == null || s != mine);
        _blackoutScreen = start < 0 ? 0 : start;

        _blackout = window;
        BlackoutButton.ToolTip = "Leave the focus screen";
        window.SetLook(_settingsStore.Load().FocusLook);
        window.Show();
        PlaceBlackout(_blackoutScreen);
        UpdateBlackout();
        window.SetTasks(OpenTasksToday(), _activeTask);
    }

    private void PlaceBlackout(int index)
    {
        if (_blackout is not { } window) return;
        var screens = DisplayInfo.All();
        if (screens.Count == 0) return;
        _blackoutScreen = (index % screens.Count + screens.Count) % screens.Count;
        DisplayInfo.Cover(window, screens[_blackoutScreen], topmost: true);
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
    private Rect _restoreBounds;
    private WindowState _restoreState;

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _restoreState = WindowState;
            _restoreBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            var screen = DisplayInfo.Of(this);
            if (screen == null) return;
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
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            WindowState = _restoreState;
        }
    }
}
