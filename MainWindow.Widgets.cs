using System.Windows;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Views;

namespace BijouHub;

// Desktop widgets: the task list and a start-timer, small always-on-top windows to keep on a second
// monitor. Each can come back when BijouHub starts, and remembers where it was left. Pressing Start
// on the timer widget starts the session and the pop-out timer takes the widget's place; when the
// session ends the widget returns to the same spot.
public partial class MainWindow
{
    private TimerWidgetWindow? _timerWidget;
    private bool _timerWidgetWanted;      // the widget is on (it's hidden while the pop-out timer stands in for it)
    private (int X, int Y)? _lastPopoutSpot;
    private bool _shuttingDown;

    // A dialog's owner is the main window, unless it isn't on screen (BijouHub started quietly in the
    // tray for its widgets): a window that was never shown can't own anything.
    private Window? DialogOwner => IsVisible ? this : null;

    private bool AnyWidgetOpen => _tasksPopout != null || _timerWidgetWanted;

    // At launch: whichever widgets were set to come back.
    public void OpenStartupWidgets()
    {
        var settings = _settingsStore.Load();
        if (settings.TaskWidget.ShowAtStartup) SetTaskWidget(true);
        if (settings.TimerWidget.ShowAtStartup) SetTimerWidget(true);
    }

    private void SetTaskWidget(bool on)
    {
        if (on != (_tasksPopout != null)) ToggleTasksPopout();
    }

    private void SetTimerWidget(bool on)
    {
        _timerWidgetWanted = on;
        if (!on)
        {
            _timerWidget?.Close();
            return;
        }
        if (_timerWidget == null) CreateTimerWidget();
        UpdateTimerWidgetOptions();
        UpdateTimerWidget();
        if (_timerPopout == null) _timerWidget!.Show(); // while the pop-out timer's up, it stands in for the widget
    }

    private void CreateTimerWidget()
    {
        var widget = new TimerWidgetWindow();
        WidgetPlacement.Attach(widget, _settingsStore.Load().TimerWidget, false, place =>
        {
            var settings = _settingsStore.Load();
            settings.TimerWidget.X = place.X;
            settings.TimerWidget.Y = place.Y;
            _settingsStore.Save(settings);
        });
        widget.StartRequested += async (project, mode, minutes) =>
        {
            var settings = _settingsStore.Load();
            settings.WidgetProjectId = project?.Id;
            settings.WidgetModeId = mode?.Id;
            settings.WidgetMinutes = minutes ?? 0;
            _settingsStore.Save(settings);

            await BeginSession(mode, project, null, minutes, countDown: minutes != null);
            SetTimerPopout(true); // the timer takes the widget's place
        };
        widget.ShowTimerRequested += () => SetTimerPopout(true);
        widget.FinishRequested += () => FinishSession_Click(this, new RoutedEventArgs());
        widget.Closed += (_, _) =>
        {
            _timerWidget = null;
            _timerWidgetWanted = false;
        };
        _timerWidget = widget;
    }

    // The projects and modes to choose from; keeps what's picked, or what was started last time.
    private void UpdateTimerWidgetOptions()
    {
        if (_timerWidget is not { } widget) return;
        var settings = _settingsStore.Load();
        var current = widget.HasOptions ? widget.Choices : (settings.WidgetProjectId, settings.WidgetModeId, settings.WidgetMinutes);
        widget.SetOptions(_projects, _modes, current.Item1, current.Item2, current.Item3);
    }

    // Shows the running session's time on the widget (and back to the start form when none is).
    private void UpdateTimerWidget()
    {
        if (_timerWidget is not { } widget) return;
        if (IsSessionActive) widget.SetRunning(true, _activeProject?.Name ?? _activeMode?.Name ?? "Session", TimerDisplay.Text);
        else widget.SetRunning(false);
    }

    // The pop-out timer is going up: it appears where the widget was, and the widget steps aside.
    private (int X, int Y)? WidgetStepsAsideFor(TimerPopoutWindow popout)
    {
        if (_timerWidget is not { IsVisible: true } widget) return null;
        var spot = WidgetPlacement.Position(widget);
        widget.Hide();
        return spot;
    }

    // The pop-out timer has gone (finished or docked): the widget comes back where the timer was.
    private void WidgetReturnsAfterPopout()
    {
        if (_shuttingDown || !_timerWidgetWanted || _timerWidget is not { } widget) return;
        UpdateTimerWidgetOptions();
        UpdateTimerWidget();
        widget.Show();
        if (_lastPopoutSpot is { } spot) WidgetPlacement.MoveTo(widget, spot.X, spot.Y);
    }

    private void OpenWidgets_Click(object sender, RoutedEventArgs e)
    {
        BringToFront();
        new WidgetsWindow(_tasksPopout != null, _timerWidgetWanted, SetTaskWidget, SetTimerWidget) { Owner = DialogOwner }.ShowDialog();
    }
}
