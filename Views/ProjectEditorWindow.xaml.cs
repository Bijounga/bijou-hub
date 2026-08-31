using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        var goal = new Goal { Name = "New Goal", Weight = 0 };
        _working.Goals.Add(goal);
        NormalizeAfterAdd(_working.Goals, goal);
        RefreshGoals();
    }

    private void AddSubGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Goal parent }) return;
        var subGoal = new Goal { Name = "New Sub-goal", Weight = 0 };
        parent.SubGoals.Add(subGoal);
        NormalizeAfterAdd(parent.SubGoals, subGoal);
        RefreshGoals();
    }

    private void RemoveGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Goal goal }) return;
        _working.Goals.Remove(goal);
        NormalizeAfterRemove(_working.Goals);
        RefreshGoals();
    }

    private void RemoveSubGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Goal subGoal }) return;
        foreach (var goal in _working.Goals)
        {
            if (goal.SubGoals.Remove(subGoal))
            {
                NormalizeAfterRemove(goal.SubGoals);
                break;
            }
        }
        RefreshGoals();
    }

    private void WeightBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: Goal goal } textBox) return;

        var listBox = FindAncestor<ListBox>(textBox);
        if (listBox == null) return;

        var siblings = listBox == GoalsList ? _working.Goals
            : listBox.Tag is Goal parent ? parent.SubGoals
            : null;
        if (siblings == null || !siblings.Contains(goal)) return;

        NormalizeAfterEdit(siblings, goal);
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current != null && current is not T)
            current = VisualTreeHelper.GetParent(current);
        return current as T;
    }

    // Keeps sibling goal weights summing to 100, redistributing proportionally
    // to each sibling's current share rather than a blind even split.
    private static void NormalizeAfterAdd(ObservableCollection<Goal> siblings, Goal added)
    {
        if (siblings.Count <= 1)
        {
            added.Weight = 100;
            return;
        }

        var fairShare = Math.Round(100.0 / siblings.Count);
        var others = siblings.Where(g => g != added).ToList();
        var othersSum = others.Sum(g => g.Weight);
        var remaining = 100 - fairShare;

        if (othersSum > 0)
        {
            var scale = remaining / othersSum;
            foreach (var g in others)
                g.Weight = Math.Round(g.Weight * scale);
        }
        else
        {
            var even = Math.Round(remaining / others.Count);
            foreach (var g in others)
                g.Weight = even;
        }

        added.Weight = fairShare;
        FixRoundingDrift(siblings, added);
    }

    private static void NormalizeAfterRemove(ObservableCollection<Goal> siblings)
    {
        if (siblings.Count == 0) return;

        var sum = siblings.Sum(g => g.Weight);
        if (sum <= 0)
        {
            var even = Math.Round(100.0 / siblings.Count);
            foreach (var g in siblings)
                g.Weight = even;
        }
        else
        {
            var scale = 100.0 / sum;
            foreach (var g in siblings)
                g.Weight = Math.Round(g.Weight * scale);
        }

        FixRoundingDrift(siblings, siblings[^1]);
    }

    private static void NormalizeAfterEdit(ObservableCollection<Goal> siblings, Goal edited)
    {
        if (siblings.Count <= 1)
        {
            edited.Weight = 100;
            return;
        }

        var clamped = Math.Clamp(edited.Weight, 0, 100);
        edited.Weight = clamped;

        var others = siblings.Where(g => g != edited).ToList();
        var remaining = 100 - clamped;
        var othersSum = others.Sum(g => g.Weight);

        if (othersSum > 0)
        {
            var scale = remaining / othersSum;
            foreach (var g in others)
                g.Weight = Math.Max(0, Math.Round(g.Weight * scale));
        }
        else
        {
            var even = Math.Round(remaining / others.Count);
            foreach (var g in others)
                g.Weight = even;
        }

        FixRoundingDrift(siblings, edited);
    }

    // Whole-number rounding can leave the total a point or two off 100; dump the
    // drift onto whichever sibling wasn't the one the user just deliberately set.
    private static void FixRoundingDrift(ObservableCollection<Goal> siblings, Goal exempt)
    {
        var drift = 100 - siblings.Sum(g => g.Weight);
        if (drift == 0) return;

        var target = siblings.FirstOrDefault(g => g != exempt) ?? siblings[0];
        target.Weight = Math.Max(0, target.Weight + drift);
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
