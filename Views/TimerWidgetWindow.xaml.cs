using System.Windows;
using System.Windows.Input;
using BijouHub.Models;

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
    public event Action? ShowTimerRequested;
    public event Action? FinishRequested;

    public TimerWidgetWindow()
    {
        InitializeComponent();
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
