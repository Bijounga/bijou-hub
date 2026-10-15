using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BijouHub.Mac.Services;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

// Turn the desktop widgets on or off, and choose which come back at startup. Changes apply at once.
// Built in code. Same behavior as Windows.
public class WidgetsWindow : Window
{
    private readonly AppSettingsStore _store = new();
    private readonly CheckBox _taskShow = new() { Content = "Show it on screen now" };
    private readonly CheckBox _timerShow = new() { Content = "Show it on screen now" };
    private readonly CheckBox _taskStartup = new() { Content = "Open it when BijouHub starts" };
    private readonly CheckBox _timerStartup = new() { Content = "Open it when BijouHub starts" };
    private readonly CheckBox _login = new() { Content = "Start BijouHub when I log in" };
    private readonly TextBlock _hint = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 6, 0, 0), IsVisible = false };
    private bool _loading = true;

    public WidgetsWindow(bool taskShown, bool timerShown, Action<bool> setTask, Action<bool> setTimer)
    {
        Title = "Widgets";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (this.TryFindResource("BgBrush", ActualThemeVariant, out var bg) && bg is IBrush bgBrush) Background = bgBrush;
        if (this.TryFindResource("HazardBrush", ActualThemeVariant, out var hazard) && hazard is IBrush hazardBrush) _hint.Foreground = hazardBrush;
        _hint.Text = "Turn this on for the widgets to appear when your computer starts. Otherwise they only appear when you open BijouHub.";

        var settings = _store.Load();
        _taskShow.IsChecked = taskShown;
        _timerShow.IsChecked = timerShown;
        _taskStartup.IsChecked = settings.TaskWidget.ShowAtStartup;
        _timerStartup.IsChecked = settings.TimerWidget.ShowAtStartup;
        _login.IsChecked = MacPlatform.StartsAtLogin;
        foreach (var (box, id) in new[] { (_taskShow, "TaskShowBox"), (_timerShow, "TimerShowBox"), (_taskStartup, "TaskStartupBox"), (_timerStartup, "TimerStartupBox"), (_login, "LoginBox") })
            Avalonia.Automation.AutomationProperties.SetAutomationId(box, id);
        Avalonia.Automation.AutomationProperties.SetAutomationId(_hint, "LoginHint");

        _taskShow.IsCheckedChanged += (_, _) => { if (!_loading) setTask(_taskShow.IsChecked == true); };
        _timerShow.IsCheckedChanged += (_, _) => { if (!_loading) setTimer(_timerShow.IsChecked == true); };
        void StartupChanged()
        {
            if (_loading) return;
            var s = _store.Load();
            s.TaskWidget.ShowAtStartup = _taskStartup.IsChecked == true;
            s.TimerWidget.ShowAtStartup = _timerStartup.IsChecked == true;
            _store.Save(s);
            UpdateHint();
        }
        _taskStartup.IsCheckedChanged += (_, _) => StartupChanged();
        _timerStartup.IsCheckedChanged += (_, _) => StartupChanged();
        _login.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            MacPlatform.SetStartsAtLogin(_login.IsChecked == true);
            UpdateHint();
        };

        Control Card(string title, string text, CheckBox show, CheckBox startup)
        {
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold });
            body.Children.Add(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.6, Margin = new Thickness(0, 0, 0, 4) });
            body.Children.Add(show);
            body.Children.Add(startup);
            var card = new Border { Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10), Child = body };
            if (this.TryFindResource("CardBrush", ActualThemeVariant, out var c) && c is IBrush cb) card.Background = cb;
            if (this.TryFindResource("SoftBorderBrush", ActualThemeVariant, out var b) && b is IBrush bb) card.BorderBrush = bb;
            return card;
        }

        var done = new Button { Content = "Done", Classes = { "primary" }, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        Avalonia.Automation.AutomationProperties.SetName(done, "Done");
        done.Click += (_, _) => Close();

        var panel = new StackPanel { Margin = new Thickness(20, 18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Small windows to keep on a second monitor. They stay on top, remember where you put them, and can come back every time BijouHub starts.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.6, Margin = new Thickness(0, 0, 0, 16)
        });
        panel.Children.Add(Card("Task list", "Today's tasks with the section tabs, an add box and a calendar view.", _taskShow, _taskStartup));
        panel.Children.Add(Card("Start timer", "Pick a project and press Start. The timer pops out in its place.", _timerShow, _timerStartup));
        _login.Margin = new Thickness(2, 6, 0, 0);
        panel.Children.Add(_login);
        panel.Children.Add(new TextBlock
        {
            Text = "With widgets set to open at startup, it starts quietly in the menu bar: just the widgets, no main window.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = 0.6, Margin = new Thickness(24, 2, 0, 0)
        });
        panel.Children.Add(_hint);
        panel.Children.Add(done);
        Content = panel;

        UpdateHint();
        _loading = false;
    }

    // Widgets set to open at startup only do so if BijouHub itself starts then.
    private void UpdateHint() =>
        _hint.IsVisible = (_taskStartup.IsChecked == true || _timerStartup.IsChecked == true) && _login.IsChecked != true;
}
