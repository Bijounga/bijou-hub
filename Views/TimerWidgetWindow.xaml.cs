using System.Windows;
using System.Windows.Input;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Views;

// A start-working widget to keep on a second monitor: pick a project (and mode, and how long), press
// Start, and the timer takes this widget's place as the pop-out timer. While a session is running it
// shows the time and a way back to the timer.
public partial class TimerWidgetWindow : Window
{
    public sealed record Length(string Label, int Minutes) { public override string ToString() => Label; }

    private static readonly Project NoProject = new() { Name = "No project" };
    private static readonly WorkMode NoMode = new() { Name = "No mode" };

    public static readonly Length[] Lengths =
    {
        new("Open-ended", 0), new("25 min", 25), new("45 min", 45), new("60 min", 60), new("90 min", 90)
    };

    public event Action<Project?, WorkMode?, int?>? StartRequested;
    public event Action<TaskPace>? PaceStartRequested;
    public event Action? ShowTimerRequested;
    public event Action? FinishRequested;

    public TimerWidgetWindow()
    {
        InitializeComponent();
        ShowTaskbarState();
        LengthBox.ItemsSource = Lengths;
        LengthBox.SelectedIndex = 0;
        ProjectBox.SelectionChanged += (_, _) => UpdateStartEnabled();
        ModeBox.SelectionChanged += (_, _) => UpdateStartEnabled();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Top + 200;
    }

    // The projects and modes to choose from, and what was picked last time.
    public void SetOptions(IEnumerable<Project> projects, IEnumerable<WorkMode> modes, string? projectId, string? modeId, int minutes)
    {
        var projectList = new[] { NoProject }.Concat(projects).ToList();
        var modeList = new[] { NoMode }.Concat(modes).ToList();
        ProjectBox.ItemsSource = projectList;
        ModeBox.ItemsSource = modeList;
        ProjectBox.SelectedItem = projectList.FirstOrDefault(p => p.Id == projectId) ?? NoProject;
        ModeBox.SelectedItem = modeList.FirstOrDefault(m => m.Id == modeId) ?? NoMode;
        LengthBox.SelectedItem = Lengths.FirstOrDefault(l => l.Minutes == minutes) ?? Lengths[0];
        UpdateStartEnabled();
    }

    public bool HasOptions => ProjectBox.ItemsSource != null;

    // The tasks with a pace, each a button that starts working on it.
    public void SetPaces(IReadOnlyList<PaceItem> items)
    {
        PaceList.Children.Clear();
        PaceSection.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in items)
        {
            var ring = new PaceRing { Width = 18, Height = 18, Fraction = item.Progress.Fraction, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var rest = new System.Windows.Controls.TextBlock { Text = item.Progress.Short, FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            rest.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "MutedTextBrush");
            var name = new System.Windows.Controls.TextBlock { Text = item.Pace.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Controls.DockPanel.SetDock(ring, System.Windows.Controls.Dock.Left);
            System.Windows.Controls.DockPanel.SetDock(rest, System.Windows.Controls.Dock.Right);
            var pace = item.Pace;
            var button = new System.Windows.Controls.Button
            {
                Content = new System.Windows.Controls.DockPanel { Children = { ring, rest, name } },
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 4),
                Opacity = item.Progress.Done || !item.Progress.Active ? 0.6 : 1,
                ToolTip = $"{item.Pace.Text}\n{item.Progress.Summary}\nClick to start working on it"
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"Pace: {item.Pace.Text}");
            button.Click += (_, _) => PaceStartRequested?.Invoke(pace);
            PaceList.Children.Add(button);
        }
    }

    public (string? ProjectId, string? ModeId, int Minutes) Choices =>
        ((ProjectBox.SelectedItem as Project)?.Id is { } p && p != NoProject.Id ? p : null,
         (ModeBox.SelectedItem as WorkMode)?.Id is { } m && m != NoMode.Id ? m : null,
         (LengthBox.SelectedItem as Length)?.Minutes ?? 0);

    // A session needs a project or a mode to be about.
    private void UpdateStartEnabled() =>
        StartButton.IsEnabled = ProjectBox.SelectedItem is Project p && p != NoProject || ModeBox.SelectedItem is WorkMode m && m != NoMode;

    public void SetRunning(bool running, string name = "", string time = "")
    {
        StartView.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        RunningView.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        HeaderText.Text = running ? "WORKING" : "START WORKING";
        if (!running) return;
        RunningName.Text = name;
        RunningTime.Text = time;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var project = ProjectBox.SelectedItem as Project;
        var mode = ModeBox.SelectedItem as WorkMode;
        var minutes = (LengthBox.SelectedItem as Length)?.Minutes ?? 0;
        StartRequested?.Invoke(project == NoProject ? null : project, mode == NoMode ? null : mode, minutes > 0 ? minutes : null);
    }

    private void ShowTimer_Click(object sender, RoutedEventArgs e) => ShowTimerRequested?.Invoke();

    // The Windows taskbar: hidden until the pointer goes to the screen edge, or always showing.
    private void Taskbar_Click(object sender, RoutedEventArgs e)
    {
        TaskbarState.AutoHide = !TaskbarState.AutoHide;
        ShowTaskbarState();
    }

    private void ShowTaskbarState()
    {
        var hidden = TaskbarState.AutoHide;
        TaskbarIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, hidden ? "AccentBrush" : "MutedTextBrush");
        TaskbarButton.ToolTip = hidden
            ? "The taskbar is hidden until you point at the screen edge. Click to show it again"
            : "Hide the taskbar (point at the screen edge to bring it back)";
    }
    private void Finish_Click(object sender, RoutedEventArgs e) => FinishRequested?.Invoke();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        for (var element = e.OriginalSource as DependencyObject; element != null && element != sender; element = System.Windows.Media.VisualTreeHelper.GetParent(element))
            if (element is System.Windows.Controls.Primitives.ButtonBase) return;
        DragMove();
    }
}
