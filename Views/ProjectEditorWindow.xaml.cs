using System.Windows;
using System.Windows.Controls;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Views;

public partial class ProjectEditorWindow : Window
{
    private readonly Project _working;
    private readonly List<WorkMode> _modes;

    public Project Project { get; }

    public ProjectEditorWindow(Project project, List<WorkMode> modes)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Project = project;
        _modes = modes;
        _working = project.Clone();

        NameBox.Text = _working.Name;
        BudgetBox.Text = _working.DefaultTargetMinutes?.ToString() ?? "";

        ModeCombo.Items.Add(new WorkMode { Id = "", Name = "(none)" });
        foreach (var mode in modes)
            ModeCombo.Items.Add(mode);
        ModeCombo.SelectedItem = ModeCombo.Items.Cast<WorkMode>()
            .FirstOrDefault(m => m.Id == (_working.LinkedModeId ?? "")) ?? ModeCombo.Items[0];

        GoalsList.ItemsSource = _working.Goals;
        ListReorderBehavior.Enable(GoalsList, _working.Goals);
    }

    private void SubGoalsList_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox { Tag: Goal parent } listBox)
            ListReorderBehavior.Enable(listBox, parent.SubGoals);
    }

    private void RefreshGoals()
    {
        GoalsList.ItemsSource = null;
        GoalsList.ItemsSource = _working.Goals;
    }

    private void AddGoal_Click(object sender, RoutedEventArgs e)
    {
        _working.Goals.Add(new Goal { Name = "New Goal", Weight = 100 });
        RefreshGoals();
    }

    private void AddSubGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Goal parent }) return;
        parent.SubGoals.Add(new Goal { Name = "New Sub-goal", Weight = 100 });
        RefreshGoals();
    }

    private void RemoveGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Goal goal }) return;
        _working.Goals.Remove(goal);
        RefreshGoals();
    }

    private void RemoveSubGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Goal subGoal }) return;
        foreach (var goal in _working.Goals)
        {
            if (goal.SubGoals.Remove(subGoal))
                break;
        }
        RefreshGoals();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Give this project a name.", "Edit Project", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _working.Name = name;
        _working.LinkedModeId = ModeCombo.SelectedItem is WorkMode { Id: not "" } selectedMode ? selectedMode.Id : null;
        _working.DefaultTargetMinutes = int.TryParse(BudgetBox.Text.Trim(), out var minutes) && minutes > 0
            ? minutes
            : null;

        Project.Name = _working.Name;
        Project.LinkedModeId = _working.LinkedModeId;
        Project.DefaultTargetMinutes = _working.DefaultTargetMinutes;
        Project.Goals.Clear();
        foreach (var goal in _working.Goals)
            Project.Goals.Add(goal);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
