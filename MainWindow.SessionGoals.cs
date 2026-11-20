using System.Windows;
using System.Windows.Controls;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub;

// Two small things on Home and the timer page: the project's goals under the running timer (so they can
// be looked at and ticked off without leaving it), and Quick Launch folding shut.
public partial class MainWindow
{
    // The goals of the project being worked on, under the timer. Nothing for a session with no project.
    private void RefreshSessionGoals()
    {
        var goals = _activeProject?.Goals;
        SessionGoalsSection.Visibility = goals is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        ApplySessionGoalsFold();
        SessionGoalsList.ItemsSource = null;
        SessionGoalsList.ItemsSource = goals; // parent completion figures recompute when it's reset
    }

    private void SessionGoal_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Goal goal } checkBox || _activeProject == null) return;

        goal.IsComplete = checkBox.IsChecked ?? false;
        _projectStore.Save(_projects);
        _boardBuiltAt = DateTime.MinValue; // the project's progress on Home changed
        PopAnimate(checkBox);
        Dispatcher.BeginInvoke(RefreshSessionGoals);
    }

    // The list folds away so the goals aren't in view all the time; the header stays to bring it back.
    private void ApplySessionGoalsFold()
    {
        var hidden = _settingsStore.Load().SessionGoalsHidden;
        SessionGoalsScroll.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        SessionGoalsChevron.Text = hidden ? "\uE76C" : "\uE70D";
    }

    private void SessionGoalsToggle_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsStore.Load();
        settings.SessionGoalsHidden = !settings.SessionGoalsHidden;
        _settingsStore.Save(settings);
        ApplySessionGoalsFold();
    }

    private void ApplyQuickLaunchFold()
    {
        var hidden = _settingsStore.Load().QuickLaunchHidden;
        QuickLaunchPanel.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        QuickLaunchChevron.Text = hidden ? "\uE76C" : "\uE70D";
    }

    private void QuickLaunchToggle_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsStore.Load();
        settings.QuickLaunchHidden = !settings.QuickLaunchHidden;
        _settingsStore.Save(settings);
        ApplyQuickLaunchFold();
    }
}
