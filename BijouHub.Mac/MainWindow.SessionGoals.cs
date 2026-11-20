using Avalonia.Interactivity;
using BijouHub.Services;

namespace BijouHub.Mac;

// Two small things on Home and the timer page: the project's goals under the running timer (so they can
// be looked at and ticked off without leaving it), and Quick Launch folding shut. Same behavior as Windows.
public partial class MainWindow
{
    // The goals of the project being worked on, under the timer. Nothing for a session with no project.
    private void RefreshSessionGoals()
    {
        var goals = _activeProject?.Goals;
        SessionGoalsSection.IsVisible = goals is { Count: > 0 };
        ApplySessionGoalsFold();
        SessionGoals.ItemsSource = null;
        SessionGoals.ItemsSource = goals; // parent completion figures recompute when it's reset
    }

    private void SessionGoal_Click(object? sender, RoutedEventArgs e)
    {
        if (_activeProject == null) return;
        _projectStore.Save(_projects.ToList());
        _boardBuiltAt = DateTime.MinValue; // the project's progress on Home changed
        RefreshSessionGoals();
    }

    // The list folds away so the goals aren't in view all the time; the header stays to bring it back.
    private void ApplySessionGoalsFold()
    {
        var hidden = _settingsStore.Load().SessionGoalsHidden;
        SessionGoals.IsVisible = !hidden;
        SessionGoalsChevron.Text = hidden ? "▸" : "▾";
    }

    private void SessionGoalsToggle_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _settingsStore.Load();
        settings.SessionGoalsHidden = !settings.SessionGoalsHidden;
        _settingsStore.Save(settings);
        ApplySessionGoalsFold();
    }

    private void ApplyQuickLaunchFold()
    {
        var hidden = _settingsStore.Load().QuickLaunchHidden;
        QuickLaunchPanel.IsVisible = !hidden;
        QuickLaunchChevron.Text = hidden ? "▸" : "▾";
    }

    private void QuickLaunchToggle_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _settingsStore.Load();
        settings.QuickLaunchHidden = !settings.QuickLaunchHidden;
        _settingsStore.Save(settings);
        ApplyQuickLaunchFold();
    }
}
