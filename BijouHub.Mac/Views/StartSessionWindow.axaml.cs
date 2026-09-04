using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BijouHub.Models;

namespace BijouHub.Mac.Views;

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

    public StartSessionWindow() : this(new Project()) { }

    public StartSessionWindow(Project project)
    {
        InitializeComponent();
        ProjectNameText.Text = project.Name;

        var goalOptions = new List<GoalOption> { new() { Label = "(no specific goal)", Goal = null } };
        foreach (var goal in project.Goals)
            AddGoalOptions(goal, 0, goalOptions);
        GoalCombo.ItemsSource = goalOptions;
        GoalCombo.SelectedIndex = 0;

        var budgetOptions = new List<string> { "No budget", "30 min", "60 min", "90 min", "Custom..." };
        BudgetCombo.ItemsSource = budgetOptions;

        if (project.DefaultTargetMinutes is int defaultMinutes)
        {
            BudgetCombo.SelectedItem = defaultMinutes switch
            {
                30 => "30 min",
                60 => "60 min",
                90 => "90 min",
                _ => "Custom..."
            };
            if ((string?)BudgetCombo.SelectedItem == "Custom...")
                CustomBudgetBox.Text = defaultMinutes.ToString();
        }
        else
        {
            BudgetCombo.SelectedIndex = 0;
        }
    }

    private static void AddGoalOptions(Goal goal, int depth, List<GoalOption> options)
    {
        var prefix = depth == 0 ? "" : new string(' ', depth * 2) + "↳ ";
        options.Add(new GoalOption { Label = prefix + goal.Name, Goal = goal });
        foreach (var sub in goal.SubGoals)
            AddGoalOptions(sub, depth + 1, options);
    }

    private void BudgetCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CustomBudgetBox.IsVisible = (string?)BudgetCombo.SelectedItem == "Custom...";
    }

    private void Start_Click(object? sender, RoutedEventArgs e)
    {
        SelectedGoal = (GoalCombo.SelectedItem as GoalOption)?.Goal;

        TargetMinutes = (string?)BudgetCombo.SelectedItem switch
        {
            "30 min" => 30,
            "60 min" => 60,
            "90 min" => 90,
            "Custom..." => int.TryParse(CustomBudgetBox.Text?.Trim(), out var custom) && custom > 0 ? custom : null,
            _ => null
        };

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
