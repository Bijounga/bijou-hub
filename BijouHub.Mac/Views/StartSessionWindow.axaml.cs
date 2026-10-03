using Avalonia.Controls;
using Avalonia.Interactivity;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

public class GoalOption
{
    public string Label { get; set; } = "";
    public Goal? Goal { get; set; }
    public override string ToString() => Label;
}

public class ModeOption
{
    public string Label { get; set; } = "";
    public WorkMode? Mode { get; set; }
    public override string ToString() => Label;
}

public partial class StartSessionWindow : Window
{
    public WorkMode? SelectedMode { get; private set; }
    public Goal? SelectedGoal { get; private set; }
    public int? TargetMinutes { get; private set; }
    public bool CountDown { get; private set; }

    public StartSessionWindow() : this(new Project(), new List<WorkMode>()) { }

    public StartSessionWindow(Project project, IReadOnlyList<WorkMode> modes)
    {
        InitializeComponent();
        ProjectNameText.Text = project.Name;

        var modeOptions = new List<ModeOption> { new() { Label = "No mode" } };
        modeOptions.AddRange(modes.Select(m => new ModeOption { Label = m.Name, Mode = m }));
        ModeCombo.ItemsSource = modeOptions;
        ModeCombo.SelectedItem = modeOptions.FirstOrDefault(o => o.Mode?.Id == project.LinkedModeId) ?? modeOptions[0];

        var goalOptions = new List<GoalOption> { new() { Label = "No specific goal" } };
        foreach (var goal in project.Goals) AddGoalOptions(goal, 0, goalOptions);
        GoalCombo.ItemsSource = goalOptions;
        GoalCombo.SelectedIndex = 0;

        if (project.DefaultTargetMinutes is int defaultMinutes) DurationBox.Text = DurationText.Format(defaultMinutes);
    }

    private static void AddGoalOptions(Goal goal, int depth, List<GoalOption> options)
    {
        var prefix = depth == 0 ? "" : new string(' ', depth * 2) + "↳ ";
        options.Add(new GoalOption { Label = prefix + goal.Name, Goal = goal });
        foreach (var sub in goal.SubGoals) AddGoalOptions(sub, depth + 1, options);
    }

    private void DurationBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var hasLength = DurationText.TryParseMinutes(DurationBox.Text) != null;
        CountDownCheck.IsEnabled = hasLength;
        if (!hasLength) CountDownCheck.IsChecked = false;
    }

    private void Start_Click(object? sender, RoutedEventArgs e)
    {
        SelectedMode = (ModeCombo.SelectedItem as ModeOption)?.Mode;
        SelectedGoal = (GoalCombo.SelectedItem as GoalOption)?.Goal;
        TargetMinutes = DurationText.TryParseMinutes(DurationBox.Text);
        CountDown = TargetMinutes != null && CountDownCheck.IsChecked == true;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
