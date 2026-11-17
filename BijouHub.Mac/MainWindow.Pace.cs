using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BijouHub.Mac.Controls;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// Paces: a steady amount of time to put into a task each day or week. Shown as a small section in the
// sidebar (click one to start working on it), in the start-working widget, and as a ring on the task
// in the task list widget. Same behavior as Windows.
public partial class MainWindow
{
    private readonly PaceStore _paceStore = new();
    private List<TaskPace> _paces = new();
    private List<PaceItem> _paceItems = new();

    private void InitPaces()
    {
        _paces = _paceStore.Load();
        UpdatePaces();
    }

    // The running session counts toward the task it's on as it goes (it isn't in the log yet).
    private int LiveSecondsOn(string key) => IsSessionActive && _activeTask?.StableKey == key ? _activeSeconds : 0;

    // Works out where every pace stands and redraws it everywhere. Only the paces whose task is still
    // on the list and not finished are shown.
    private void UpdatePaces()
    {
        if (_paces.Count == 0 && _paceItems.Count == 0 && !_dailyGoals.Any(g => g.HasPace)) return;

        var sessions = _logService.GetAll();
        var now = DateTime.Now;
        var goalByKey = new Dictionary<string, DailyGoal>();
        foreach (var goal in _dailyGoals) goalByKey.TryAdd(goal.StableKey, goal);

        // A task renamed or moved to another project: the pace follows it.
        var changed = false;
        foreach (var pace in _paces)
        {
            if (!goalByKey.TryGetValue(pace.Key, out var goal)) continue;
            if (pace.Text != goal.Text || pace.ProjectId != goal.ProjectId || pace.ProjectName != goal.ProjectName)
            {
                pace.Text = goal.Text;
                pace.ProjectId = goal.ProjectId;
                pace.ProjectName = goal.ProjectName;
                changed = true;
            }
        }
        if (changed) _paceStore.Save(_paces);

        _paceItems = _paces
            .Where(p => goalByKey.TryGetValue(p.Key, out var goal) && !goal.Done)
            .Select(p => new PaceItem(p, PaceProgress.For(p, sessions, LiveSecondsOn(p.Key), now)))
            .OrderBy(i => i.Progress.Done || !i.Progress.Active ? 1 : 0) // still to do first; the order they were made in otherwise
            .ToList();

        var byKey = _paceItems.ToDictionary(i => i.Pace.Key);
        var displayChanged = false;
        foreach (var goal in _dailyGoals)
        {
            if (byKey.TryGetValue(goal.StableKey, out var item))
            {
                if (!goal.HasPace || Math.Abs(goal.PaceFraction - item.Progress.Fraction) > 0.001 || goal.PaceText != item.Progress.Summary) displayChanged = true;
                goal.HasPace = true;
                goal.PaceFraction = item.Progress.Fraction;
                goal.PaceText = item.Progress.Summary;
            }
            else if (goal.HasPace)
            {
                displayChanged = true;
                goal.HasPace = false;
                goal.PaceFraction = 0;
                goal.PaceText = null;
            }
        }

        RebuildPaceSidebar();
        _timerWidget?.SetPaces(_paceItems);
        if (displayChanged) RefreshTasksPopout();
    }

    private void RebuildPaceSidebar()
    {
        PacePanel.Children.Clear();
        PaceSection.IsVisible = _paceItems.Count > 0;
        foreach (var item in _paceItems) PacePanel.Children.Add(PaceRow(item));
    }

    private Button PaceRow(PaceItem item)
    {
        var ring = new PaceRing { Width = 18, Height = 18, Fraction = item.Progress.Fraction, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = item.Pace.Text, FontSize = 13, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var rest = new TextBlock { Text = item.Progress.Short, FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        DockPanel.SetDock(ring, Dock.Left);
        DockPanel.SetDock(rest, Dock.Right);

        var button = new Button
        {
            Classes = { "navrow" },
            Content = new DockPanel { Children = { ring, rest, name } },
            Opacity = item.Progress.Done || !item.Progress.Active ? 0.6 : 1
        };
        ToolTip.SetTip(button, $"{item.Pace.Text}\n{item.Progress.Summary}\nClick to start working on it");
        Avalonia.Automation.AutomationProperties.SetName(button, $"Pace: {item.Pace.Text}");
        button.Click += async (_, _) => await StartPace(item.Pace);
        return button;
    }

    // Starts a countdown for what's left of the pace (or open-ended when it's already done), on that task.
    private async Task StartPace(TaskPace pace)
    {
        var goal = _dailyGoals.FirstOrDefault(g => g.StableKey == pace.Key);
        if (goal == null) return;
        if (IsSessionActive && _activeTask?.StableKey == pace.Key)
        {
            ShowSession(); // already on it
            return;
        }

        var project = _projects.FirstOrDefault(p => p.Id == (goal.ProjectId ?? pace.ProjectId));
        var mode = project?.LinkedModeId is { } linked ? _modes.FirstOrDefault(m => m.Id == linked) : null;
        if (project == null) mode ??= _modes.FirstOrDefault(m => m.Id == _settingsStore.Load().WidgetModeId) ?? _modes.FirstOrDefault();
        if (project == null && mode == null)
        {
            await PromptWindow.Notice(this, "Pace", "Make a mode first: a session needs a project or a mode to be about.");
            return;
        }

        var progress = PaceProgress.For(pace, _logService.GetAll(), LiveSecondsOn(pace.Key), DateTime.Now);
        // A weekly pace is worked in sittings of an hour at most.
        int? minutes = progress.Done ? null : Math.Clamp(progress.RemainingMinutes, 1, pace.Rhythm == PaceRhythm.Weekly ? 60 : 24 * 60);
        await BeginSession(mode, project, null, minutes, countDown: minutes != null, task: goal);
    }

    // The task's right-click "Set a pace…".
    private async void SetPace(DailyGoal goal)
    {
        var existing = _paces.FirstOrDefault(p => p.Key == goal.StableKey);
        var dialog = new PaceWindow(goal.Text, existing);
        if (!await dialog.ShowDialog<bool>(this)) return;

        _paces.RemoveAll(p => p.Key == goal.StableKey);
        if (!dialog.Remove)
        {
            _paces.Add(new TaskPace
            {
                Key = goal.StableKey, Text = goal.Text, ProjectId = goal.ProjectId, ProjectName = goal.ProjectName,
                Minutes = dialog.Minutes, Rhythm = dialog.Rhythm, CreatedOn = existing?.CreatedOn ?? DateTime.Today
            });
        }
        _paceStore.Save(_paces);
        UpdatePaces();
    }
}
