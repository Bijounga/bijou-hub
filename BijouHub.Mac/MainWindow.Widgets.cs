using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BijouHub.Mac.Services;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// Desktop widgets: the task list and a start-timer, small always-on-top windows to keep on a second
// monitor. Each can come back when BijouHub starts, and remembers where it was left. Pressing Start
// on the timer widget starts the session and the pop-out timer takes the widget's place; when the
// session ends the widget returns to the same spot. Same behavior as Windows.
public partial class MainWindow
{
    private TimerWidgetWindow? _timerWidget;
    private bool _timerWidgetWanted;      // the widget is on (it's hidden while the pop-out timer stands in for it)
    private PixelPoint? _lastPopoutSpot;
    private bool _shuttingDown;

    private bool _widgetsOnTop;
    private bool _openingAtStartup;

    private void SetWidgetsOnTop(bool on)
    {
        _widgetsOnTop = on;
        var settings = _settingsStore.Load();
        settings.WidgetsOnTop = on;
        _settingsStore.Save(settings);
        WidgetPlacement.Reapply(_tasksPopout);
        WidgetPlacement.Reapply(_timerWidget);
    }

    private bool AnyWidgetOpen => _tasksPopout != null || _timerWidgetWanted;

    // At launch: whichever widgets were set to come back.
    public void OpenStartupWidgets()
    {
        var settings = _settingsStore.Load();
        _openingAtStartup = true; // they appear without taking focus from what you're doing
        try
        {
            if (settings.TaskWidget.ShowAtStartup) SetTaskWidget(true);
            if (settings.TimerWidget.ShowAtStartup) SetTimerWidget(true);
        }
        finally
        {
            _openingAtStartup = false;
        }
    }

    // A dialog needs the main window on screen to be its owner: if BijouHub is sitting in the menu
    // bar with just its widgets, bring the window up for the moment.
    private void EnsureOwnerVisible()
    {
        if (!IsVisible) Show();
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
        if (_popout == null) _timerWidget!.Show(); // while the pop-out timer's up, it stands in for the widget
    }

    private void CreateTimerWidget()
    {
        var widget = new TimerWidgetWindow { ShowActivated = !_openingAtStartup };
        WidgetPlacement.Layer(widget, () => _widgetsOnTop);
        widget.Positioned = WidgetPlacement.Attach(widget, _settingsStore.Load().TimerWidget, false, () => Screens, place =>
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
        widget.FinishRequested += () => _ = FinishSessionAsync(askForNote: true);
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
        if (IsSessionActive) widget.SetRunning(true, _activeProject?.Name ?? _activeMode?.Name ?? "Session", TimerDisplay);
        else widget.SetRunning(false);
    }

    // The pop-out timer is going up: it appears where the widget was, and the widget steps aside.
    private PixelPoint? WidgetStepsAside()
    {
        if (_timerWidget is not { IsVisible: true } widget) return null;
        var spot = widget.Position;
        widget.Hide();
        return spot;
    }

    // The pop-out timer has gone (finished or docked): the widget comes back where the timer was.
    private void WidgetReturnsAfterPopout()
    {
        if (_shuttingDown || !_timerWidgetWanted || _timerWidget is not { } widget) return;
        UpdateTimerWidgetOptions();
        UpdateTimerWidget();
        if (_lastPopoutSpot is { } spot && WidgetPlacement.OnScreen(Screens, spot)) widget.Position = spot;
        widget.Show();
    }

    // Brings the widgets that are open onto the screen the main window is on (or the main screen):
    // for when one was left on a monitor that's gone, off to the side, or squashed. They come to the
    // front for a few seconds so you can see where they landed.
    private void RecenterWidgets()
    {
        var screen = (IsVisible ? Screens.ScreenFromWindow(this) : null) ?? Screens.Primary;
        if (screen == null) return;
        var area = screen.WorkingArea;
        foreach (var widget in new Window?[] { _tasksPopout, _timerWidget })
        {
            if (widget is not { IsVisible: true }) continue;
            if (widget is TimerWidgetWindow) widget.Width = 250; // a squashed one gets its width back
            var width = (int)(widget.Bounds.Width * screen.Scaling);
            var height = (int)(widget.Bounds.Height * screen.Scaling);
            widget.Position = new PixelPoint(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2);
            widget.Topmost = true;
            var back = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            back.Tick += (_, _) => { back.Stop(); WidgetPlacement.Reapply(widget); };
            back.Start();
        }
        if (_timerWidgetWanted && _timerWidget is { IsVisible: false } && _popout == null) _timerWidget.Show();
    }

    private async void OpenWidgets_Click(object? sender, RoutedEventArgs e)
    {
        EnsureOwnerVisible();
        await new WidgetsWindow(_tasksPopout != null, _timerWidgetWanted, SetTaskWidget, SetTimerWidget, _widgetsOnTop, SetWidgetsOnTop, RecenterWidgets).ShowDialog(this);
    }
}
