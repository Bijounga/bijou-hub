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
        BudgetCombo.Items.Add("Custom...");

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
        CustomBudgetBox.Visibility = BudgetCombo.SelectedItem as string == "Custom..."
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateCountDownAvailability();
    }

    private void UpdateCountDownAvailability()
    {
        var hasBudget = BudgetCombo.SelectedItem as string != "No budget";
        CountDownCheckBox.IsEnabled = hasBudget;
        if (!hasBudget) CountDownCheckBox.IsChecked = false;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = ModeCombo.SelectedItem is WorkMode { Id: not "" } selectedMode ? selectedMode : null;
        SelectedGoal = (GoalCombo.SelectedItem as GoalOption)?.Goal;

        TargetMinutes = BudgetCombo.SelectedItem as string switch
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
