using System.Windows;
using BijouHub.Services;

namespace BijouHub.Views;

// Turn the desktop widgets on or off, and choose which come back at startup. Changes apply at once.
public partial class WidgetsWindow : Window
{
    private readonly AppSettingsStore _store = new();
    private readonly Action<bool> _setTask;
    private readonly Action<bool> _setTimer;

    private readonly Action<bool> _setOnTop;
    private readonly Action _recenter;

    public WidgetsWindow(bool taskShown, bool timerShown, Action<bool> setTask, Action<bool> setTimer, bool onTop, Action<bool> setOnTop, Action recenter)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _loading = true;
        _recenter = recenter;
        _setOnTop = setOnTop;
        _setTask = setTask;
        _setTimer = setTimer;
        var settings = _store.Load();
        TaskShowBox.IsChecked = taskShown;
        TimerShowBox.IsChecked = timerShown;
        TaskStartupBox.IsChecked = settings.TaskWidget.ShowAtStartup;
        TimerStartupBox.IsChecked = settings.TimerWidget.ShowAtStartup;
        OnTopBox.IsChecked = onTop;
        LoginBox.IsChecked = StartupService.IsEnabled;
        UpdateHint();
        _loading = false;
    }

    private bool _loading;

    private void TaskShow_Click(object sender, RoutedEventArgs e) { if (!_loading) _setTask(TaskShowBox.IsChecked == true); }
    private void TimerShow_Click(object sender, RoutedEventArgs e) { if (!_loading) _setTimer(TimerShowBox.IsChecked == true); }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var settings = _store.Load();
        settings.TaskWidget.ShowAtStartup = TaskStartupBox.IsChecked == true;
        settings.TimerWidget.ShowAtStartup = TimerStartupBox.IsChecked == true;
        _store.Save(settings);

        UpdateHint();
    }

    private void OnTop_Click(object sender, RoutedEventArgs e) { if (!_loading) _setOnTop(OnTopBox.IsChecked == true); }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        StartupService.SetEnabled(LoginBox.IsChecked == true);
        UpdateHint();
    }

    // Widgets set to open at startup only do so if BijouHub itself starts then.
    private void UpdateHint() =>
        LoginHint.Visibility = (TaskStartupBox.IsChecked == true || TimerStartupBox.IsChecked == true) && LoginBox.IsChecked != true
            ? Visibility.Visible : Visibility.Collapsed;

    private void Recenter_Click(object sender, RoutedEventArgs e) => _recenter();

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
