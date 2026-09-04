using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using BijouHub.Models;

namespace BijouHub.Mac.Views;

public partial class ProjectEditorWindow : Window
{
    public static readonly IValueConverter IsZeroConverter =
        new FuncValueConverter<int, bool>(count => count == 0);
    public static readonly IValueConverter IsNotZeroConverter =
        new FuncValueConverter<int, bool>(count => count != 0);

    private readonly Project _project;

    public ProjectEditorWindow() : this(new Project()) { }

    public ProjectEditorWindow(Project project)
    {
        InitializeComponent();
        _project = project;

        NameBox.Text = project.Name;
        BudgetBox.Text = project.DefaultTargetMinutes?.ToString(CultureInfo.InvariantCulture) ?? "";
        GoalsList.ItemsSource = project.Goals;
    }

    private void AddGoal_Click(object? sender, RoutedEventArgs e)
    {
        _project.Goals.Add(new Goal { Name = "New Goal", Weight = 100 });
    }

    private void RemoveGoal_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Goal goal })
            _project.Goals.Remove(goal);
    }

    private void AddSubGoal_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Goal goal })
            goal.SubGoals.Add(new Goal { Name = "New Sub-Goal", Weight = 100 });
    }

    private void RemoveSubGoal_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Goal subGoal }) return;
        foreach (var goal in _project.Goals)
        {
            if (goal.SubGoals.Remove(subGoal)) return;
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (!string.IsNullOrEmpty(name))
            _project.Name = name;

        _project.DefaultTargetMinutes = int.TryParse(BudgetBox.Text, out var minutes) ? minutes : null;

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
