using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BijouHub.Mac.Views;
using BijouHub.Models;

namespace BijouHub.Mac;

// The focus screen: everything blacked out, just the timer in the middle with the plan in small type
// under it, for leaving on a second monitor. It opens on a monitor other than the one BijouHub is
// on (if there is one). And F11 takes the main window full screen. Same behavior as Windows.
public partial class MainWindow
{
    private BlackoutWindow? _blackout;
    private int _blackoutScreen;

    private void Blackout_Click(object? sender, RoutedEventArgs e) => ToggleBlackout();

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
        window.FinishRequested += () => _ = FinishSessionAsync(askForNote: true);
        window.ScreenChosen += index => PlaceBlackout(index);
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
            ToolTip.SetTip(BlackoutButton, "Black out the screen and show just the timer");
        };

        // Another monitor than the one BijouHub is on, if there is one: that's the one to leave it on.
        var screens = Screens.All;
        var mine = IsVisible ? Screens.ScreenFromWindow(this) : null;
        var start = -1;
        for (var i = 0; i < screens.Count && start < 0; i++)
            if (mine == null || screens[i].Bounds != mine.Bounds) start = i;
        _blackoutScreen = Math.Max(0, start);

        window.SetLook(_settingsStore.Load().FocusLook);
        _blackout = window;
        ToolTip.SetTip(BlackoutButton, "Leave the focus screen");
        PlaceBlackout(_blackoutScreen, show: true);
        UpdateBlackout();
        window.SetTasks(OpenTasksToday(), _activeTask);
    }

    // Covers the chosen screen: moved onto it, then full screen there.
    private void PlaceBlackout(int index, bool show = false)
    {
        if (_blackout is not { } window) return;
        var screens = Screens.All;
        if (screens.Count == 0) return;
        _blackoutScreen = (index % screens.Count + screens.Count) % screens.Count;
        var screen = screens[_blackoutScreen];

        var mine = IsVisible ? Screens.ScreenFromWindow(this) : null;
        window.SetScreens(screens.Select((s, i) =>
            $"Screen {i + 1}: {s.Bounds.Width}×{s.Bounds.Height}" + (s.IsPrimary ? ", main" : "") + (mine != null && s.Bounds == mine.Bounds ? " — BijouHub is on this one" : "")).ToList(), _blackoutScreen);

        window.WindowState = WindowState.Normal;
        window.Position = screen.Bounds.TopLeft;
        if (show) window.Show();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => window.WindowState = WindowState.FullScreen, Avalonia.Threading.DispatcherPriority.Background);
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
        _blackout.Update(TimerDisplay, SessionStatusText.Text ?? "", _paused, BlackoutGoalText());
    }

    // ---------- F11: full screen ----------

    private WindowState _stateBeforeFullscreen = WindowState.Normal;

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _stateBeforeFullscreen;
            return;
        }
        _stateBeforeFullscreen = WindowState;
        WindowState = WindowState.FullScreen;
    }
}
