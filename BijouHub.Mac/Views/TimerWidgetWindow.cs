using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BijouHub.Models;

namespace BijouHub.Mac.Views;

// A start-working widget to keep on a second monitor: pick a project (and mode, and how long), press
// Start, and the timer takes this widget's place as the pop-out timer. While a session is running it
// shows the time and a way back to the timer. Built in code. Same behavior as Windows.
public class TimerWidgetWindow : Window
{
    public sealed record Length(string Label, int Minutes) { public override string ToString() => Label; }

    private static readonly Project NoProject = new() { Name = "No project" };
    private static readonly WorkMode NoMode = new() { Name = "No mode" };

    public static readonly Length[] Lengths =
    {
        new("Open-ended", 0), new("25 min", 25), new("45 min", 45), new("60 min", 60), new("90 min", 90)
    };

    public event Action<Project?, WorkMode?, int?>? StartRequested;
    public event Action? ShowTimerRequested;
    public event Action? FinishRequested;

    private readonly ComboBox _projectBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _modeBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _lengthBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Lengths };
    private readonly Button _start = new() { Content = "▶  Start", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(12, 8) };
    private readonly StackPanel _startView = new();
    private readonly StackPanel _runningView = new() { IsVisible = false };
    private readonly TextBlock _header = new() { Text = "START WORKING", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
    private readonly TextBlock _runningName = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _runningTime = new() { Text = "00:00:00", FontSize = 30, Margin = new Thickness(0, 2, 0, 10) };

    public TimerWidgetWindow()
    {
        Title = "BijouHub Start Timer";
        Width = 250;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        SystemDecorations = SystemDecorations.BorderOnly;
        if (this.TryFindResource("BgBrush", ActualThemeVariant, out var bg) && bg is IBrush bgBrush) Background = bgBrush;
        if (this.TryFindResource("TimerFont", ActualThemeVariant, out var font) && font is FontFamily family) _runningTime.FontFamily = family;
        if (this.TryFindResource("AccentBrush", ActualThemeVariant, out var accent) && accent is IBrush accentBrush) _runningTime.Foreground = accentBrush;

        Avalonia.Automation.AutomationProperties.SetName(_projectBox, "Widget project");
        Avalonia.Automation.AutomationProperties.SetName(_modeBox, "Widget mode");
        Avalonia.Automation.AutomationProperties.SetName(_lengthBox, "Widget length");
        Avalonia.Automation.AutomationProperties.SetName(_start, "Start timer");
        _lengthBox.SelectedIndex = 0;
        _projectBox.SelectionChanged += (_, _) => UpdateStartEnabled();
        _modeBox.SelectionChanged += (_, _) => UpdateStartEnabled();
        _start.Click += (_, _) =>
        {
            var project = _projectBox.SelectedItem as Project;
            var mode = _modeBox.SelectedItem as WorkMode;
            var minutes = (_lengthBox.SelectedItem as Length)?.Minutes ?? 0;
            StartRequested?.Invoke(project == NoProject ? null : project, mode == NoMode ? null : mode, minutes > 0 ? minutes : null);
        };

        TextBlock Label(string text) => new() { Text = text, FontSize = 10, Margin = new Thickness(0, 0, 0, 3), Opacity = 0.6 };
        _startView.Children.Add(Label("PROJECT"));
        _startView.Children.Add(_projectBox);
        _projectBox.Margin = new Thickness(0, 0, 0, 8);
        _startView.Children.Add(Label("MODE"));
        _startView.Children.Add(_modeBox);
        _modeBox.Margin = new Thickness(0, 0, 0, 8);
        _startView.Children.Add(Label("FOR"));
        _startView.Children.Add(_lengthBox);
        _lengthBox.Margin = new Thickness(0, 0, 0, 12);
        _startView.Children.Add(_start);

        var showTimer = new Button { Content = "Show timer", Padding = new Thickness(12, 6) };
        Avalonia.Automation.AutomationProperties.SetName(showTimer, "Show timer");
        showTimer.Click += (_, _) => ShowTimerRequested?.Invoke();
        var finish = new Button { Content = "Finish", Padding = new Thickness(12, 6) };
        Avalonia.Automation.AutomationProperties.SetName(finish, "Finish timer");
        finish.Click += (_, _) => FinishRequested?.Invoke();
        _runningView.Children.Add(_runningName);
        _runningView.Children.Add(_runningTime);
        _runningView.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { showTimer, finish } });

        var close = new Button { Content = "✕", Classes = { "ghost" }, Padding = new Thickness(6, 2) };
        ToolTip.SetTip(close, "Hide this widget");
        Avalonia.Automation.AutomationProperties.SetName(close, "Close timer widget");
        close.Click += (_, _) => Close();
        var header = new DockPanel { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(_header);
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not (Button or TextBlock { Parent: Border or Button })) BeginMoveDrag(e);
        };

        var frame = new Border { BorderThickness = new Thickness(1.5), Child = new StackPanel { Margin = new Thickness(14, 10, 14, 14), Children = { header, _startView, _runningView } } };
        if (accent is IBrush frameBrush) frame.BorderBrush = frameBrush;
        Content = frame;

        Opened += (_, _) =>
        {
            if (!Positioned && Screens.Primary is { } screen)
                Position = new PixelPoint(screen.WorkingArea.Right - (int)(Width * screen.Scaling) - 16, screen.WorkingArea.Y + 200);
        };
    }

    public bool Positioned { get; set; }
    public bool HasOptions => _projectBox.ItemsSource != null;

    public void SetOptions(IEnumerable<Project> projects, IEnumerable<WorkMode> modes, string? projectId, string? modeId, int minutes)
    {
        var projectList = new[] { NoProject }.Concat(projects).ToList();
        var modeList = new[] { NoMode }.Concat(modes).ToList();
        _projectBox.ItemsSource = projectList;
        _modeBox.ItemsSource = modeList;
        _projectBox.SelectedItem = projectList.FirstOrDefault(p => p.Id == projectId) ?? NoProject;
        _modeBox.SelectedItem = modeList.FirstOrDefault(m => m.Id == modeId) ?? NoMode;
        _lengthBox.SelectedItem = Lengths.FirstOrDefault(l => l.Minutes == minutes) ?? Lengths[0];
        UpdateStartEnabled();
    }

    public (string? ProjectId, string? ModeId, int Minutes) Choices =>
        ((_projectBox.SelectedItem as Project)?.Id is { } p && p != NoProject.Id ? p : null,
         (_modeBox.SelectedItem as WorkMode)?.Id is { } m && m != NoMode.Id ? m : null,
         (_lengthBox.SelectedItem as Length)?.Minutes ?? 0);

    // A session needs a project or a mode to be about.
    private void UpdateStartEnabled() =>
        _start.IsEnabled = _projectBox.SelectedItem is Project p && p != NoProject || _modeBox.SelectedItem is WorkMode m && m != NoMode;

    public void SetRunning(bool running, string name = "", string time = "")
    {
        _startView.IsVisible = !running;
        _runningView.IsVisible = running;
        _header.Text = running ? "WORKING" : "START WORKING";
        if (!running) return;
        _runningName.Text = name;
        _runningTime.Text = time;
    }
}
