using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Views;

public class GoalOption
{
    public string Label { get; set; } = "";
    public Goal? Goal { get; set; }

    public override string ToString() => Label;
}

public partial class StartSessionWindow : Window
{
    public Goal? SelectedGoal { get; private set; }
    public int? TargetMinutes { get; private set; }
    public bool CountDown { get; private set; }
    public WorkMode? SelectedMode { get; private set; }
    public PomodoroPlan? Pomodoro { get; private set; }

    private const string CustomMinutes = "Custom...";
    private const string Pomodoro25 = "Pomodoro 25 / 5  ×4";
    private const string Pomodoro50 = "Pomodoro 50 / 10  ×3";
    private const string PomodoroCustom = "Pomodoro custom...";

    public StartSessionWindow(Project project, List<WorkMode> modes)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        ProjectNameText.Text = project.Name;

        ModeCombo.Items.Add(new WorkMode { Id = "", Name = "(none)" });
        foreach (var mode in modes)
            ModeCombo.Items.Add(mode);
        ModeCombo.SelectedItem = ModeCombo.Items.Cast<WorkMode>()
            .FirstOrDefault(m => m.Id == (project.LinkedModeId ?? "")) ?? ModeCombo.Items[0];

        GoalCombo.Items.Add(new GoalOption { Label = "(no specific goal)", Goal = null });
        foreach (var goal in project.Goals)
            AddGoalOptions(goal, 0);
        GoalCombo.SelectedIndex = 0;

        BudgetCombo.Items.Add("No budget");
        BudgetCombo.Items.Add("30 min");
        BudgetCombo.Items.Add("60 min");
        BudgetCombo.Items.Add("90 min");
        BudgetCombo.Items.Add(CustomMinutes);
        BudgetCombo.Items.Add(Pomodoro25);
        BudgetCombo.Items.Add(Pomodoro50);
        BudgetCombo.Items.Add(PomodoroCustom);

        if (project.DefaultTargetMinutes is int defaultMinutes)
        {
            BudgetCombo.SelectedItem = defaultMinutes switch
            {
                30 => "30 min",
                60 => "60 min",
                90 => "90 min",
                _ => "Custom..."
            };
            if (BudgetCombo.SelectedItem as string == "Custom...")
                CustomBudgetBox.Text = defaultMinutes.ToString();
        }
        else
        {
            BudgetCombo.SelectedIndex = 0;
        }

        UpdateCountDownAvailability();
    }

    private void AddGoalOptions(Goal goal, int depth)
    {
        var prefix = depth == 0 ? "" : new string(' ', depth * 2) + "↳ ";
        GoalCombo.Items.Add(new GoalOption { Label = prefix + goal.Name, Goal = goal });
        foreach (var sub in goal.SubGoals)
            AddGoalOptions(sub, depth + 1);
    }

    private void BudgetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var choice = BudgetCombo.SelectedItem as string;
        CustomBudgetBox.Visibility = choice is CustomMinutes or PomodoroCustom ? Visibility.Visible : Visibility.Collapsed;
        CustomBudgetBox.ToolTip = choice == PomodoroCustom ? "Focus/break, e.g. 25/5 or 25/5x4 (rounds)" : "Minutes";
        if (choice == PomodoroCustom && CustomBudgetBox.Text.Length == 0) CustomBudgetBox.Text = "25/5x4";
        UpdateCountDownAvailability();
    }

    private void UpdateCountDownAvailability()
    {
        var choice = BudgetCombo.SelectedItem as string;
        var hasBudget = choice != "No budget" && choice?.StartsWith("Pomodoro") != true;
        CountDownCheckBox.IsEnabled = hasBudget;
        if (!hasBudget) CountDownCheckBox.IsChecked = false;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = ModeCombo.SelectedItem is WorkMode { Id: not "" } selectedMode ? selectedMode : null;
        SelectedGoal = (GoalCombo.SelectedItem as GoalOption)?.Goal;

        Pomodoro = BudgetCombo.SelectedItem as string switch
        {
            Pomodoro25 => new PomodoroPlan(25, 5, 4),
            Pomodoro50 => new PomodoroPlan(50, 10, 3),
            PomodoroCustom => DurationText.TryParsePomodoro(CustomBudgetBox.Text),
            _ => null
        };
        if (BudgetCombo.SelectedItem as string == PomodoroCustom && Pomodoro == null)
        {
            // not something that reads as focus/break: keep the dialog open on it
            CustomBudgetBox.Focus();
            CustomBudgetBox.SelectAll();
            return;
        }

        TargetMinutes = Pomodoro != null ? null : BudgetCombo.SelectedItem as string switch
        {
            "30 min" => 30,
            "60 min" => 60,
            "90 min" => 90,
            "Custom..." => int.TryParse(CustomBudgetBox.Text.Trim(), out var custom) && custom > 0 ? custom : null,
            _ => null
        };
        CountDown = TargetMinutes != null && CountDownCheckBox.IsChecked == true;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
