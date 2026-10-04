using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BijouHub.Mac.Controls;
using BijouHub.Models;
using BijouHub.Mac.Views;
using BijouHub.Services;
using BijouHub.Services.GoogleTasks;

namespace BijouHub.Mac;

// The Tasks page: every list at full size, like Microsoft To Do. Smart lists (Today, Important,
// Planned, All) and each category's lists on the left; the chosen list's tasks on the right with
// its own add bar. It shows the same goals as Home. Same behavior as Windows.
public partial class MainWindow
{
    private sealed record TaskView(string Key, string Title, Func<DailyGoal, bool> Includes, ListTarget? Target);

    private string _taskViewKey = "today";
    private TaskView? _taskView;
    private readonly ObservableCollection<DailyGoal> _tasksOpenItems = new();
    private readonly ObservableCollection<DailyGoal> _tasksDoneItems = new();
    private bool _showCompleted;

    private void InitTasksPage()
    {
        TasksOpenList.ItemsSource = _tasksOpenItems;
        TasksDoneList.ItemsSource = _tasksDoneItems;
    }

    private void NavHome_Click(object? sender, RoutedEventArgs e)
    {
        ModesList.SelectedItem = null;
        ProjectsList.SelectedItem = null;
        ShowHome();
    }

    private void NavTasks_Click(object? sender, RoutedEventArgs e) => ShowTasks();

    private void ShowTasks()
    {
        ModesList.SelectedItem = null;
        ProjectsList.SelectedItem = null;
        _detailProject = null;
        ShowOnly(TasksPanel);
        LoadDailyPlan();
        RefreshTasksPage();
        _ = RefreshGoogleGoalsAsync();
        TasksInput.Focus();
    }

    private IEnumerable<TaskView> TaskViews()
    {
        yield return new TaskView("today", "Today", g => !g.IsPlannedAfter(TodayKey), null);
        yield return new TaskView("important", "Important", g => g.Starred, null);
        yield return new TaskView("planned", "Planned", g => g.Due != null, null);
        yield return new TaskView("all", "All tasks", _ => true, null);
        foreach (var group in ScopeGroups())
            foreach (var target in TargetsFor(group))
                yield return new TaskView("list:" + target.Key, target.Name ?? "General", g => IsTargetOf(target, g), target);
    }

    private static string IconFor(string key) => key switch
    {
        "today" => "sun",
        "important" => "star",
        "planned" => "calendar",
        "all" => "all",
        _ => "list"
    };

    private void RefreshTasksPage()
    {
        var openTotal = _dailyGoals.Count(g => !g.Done);
        NavTasksCount.Text = openTotal > 0 ? openTotal.ToString() : "";
        if (!TasksPanel.IsVisible) return;

        var views = TaskViews().ToList();
        _taskView = views.FirstOrDefault(v => v.Key == _taskViewKey) ?? views[0];
        _taskViewKey = _taskView.Key;
        BuildTaskListsNav(views);

        Sync(_tasksOpenItems, _dailyGoals.Where(g => !g.Done && _taskView.Includes(g)));
        Sync(_tasksDoneItems, _dailyGoals.Where(g => g.Done && _taskView.Includes(g)));

        TasksTitle.Text = _taskView.Target is { Group: var group } && group != EditingGroup && GoogleMode
            ? $"{GroupLabel(group)} · {_taskView.Title}"
            : _taskView.Title;
        TasksCountText.Text = _tasksOpenItems.Count == 0 ? "" : $"{_tasksOpenItems.Count} open";
        TasksListMenuButton.IsVisible = GoogleMode && _taskView.Target != null;
        TasksEmptyText.IsVisible = _tasksOpenItems.Count == 0;
        TasksCompletedToggle.IsVisible = _tasksDoneItems.Count > 0;
        TasksCompletedText.Text = $"{(_showCompleted ? "▾" : "▸")}  Completed today  {_tasksDoneItems.Count}";
        TasksDoneList.IsVisible = _showCompleted && _tasksDoneItems.Count > 0;
        TasksInputPlaceholder.Text = _taskView.Key switch
        {
            "important" => "Add an important task",
            "planned" => "Add a task for today",
            _ when _taskView.Target != null => $"Add a task to {_taskView.Title}",
            _ => "Add a task"
        };
    }

    // Only touches the shown list when what's in it actually changed.
    private static void Sync(ObservableCollection<DailyGoal> shown, IEnumerable<DailyGoal> wanted)
    {
        var desired = wanted.ToList();
        if (desired.SequenceEqual(shown)) return;
        shown.Clear();
        foreach (var goal in desired) shown.Add(goal);
    }

    private void BuildTaskListsNav(List<TaskView> views)
    {
        TaskListsNav.Children.Clear();
        string? lastGroup = "";
        foreach (var view in views)
        {
            var group = view.Target?.Group;
            if (view.Target != null && group != lastGroup)
            {
                var header = new TextBlock { Text = (GoogleMode ? GroupLabel(group!) : "Projects").ToUpperInvariant(), FontSize = 10, Margin = new Thickness(10, 16, 0, 4) };
                header.Classes.Add("muted");
                TaskListsNav.Children.Add(header);
                lastGroup = group;
            }
            TaskListsNav.Children.Add(BuildTaskListRow(view));
        }
    }

    private Button BuildTaskListRow(TaskView view)
    {
        var selected = view.Key == _taskViewKey;
        var count = _dailyGoals.Count(g => !g.Done && view.Includes(g));

        var number = new TextBlock { Text = count > 0 ? count.ToString() : "", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        number.Classes.Add("muted");
        DockPanel.SetDock(number, Dock.Right);
        var button = new Button
        {
            Content = new DockPanel
            {
                Children =
                {
                    number,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new LineIcon { Kind = IconFor(view.Key), Width = 14, Height = 14, Foreground = Brush(selected ? "AccentBrush" : "MutedTextBrush") },
                            new TextBlock { Text = view.Title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }
                        }
                    }
                }
            }
        };
        button.Classes.Add("navrow");
        button.Classes.Set("on", selected);
        Avalonia.Automation.AutomationProperties.SetName(button, $"{view.Title} list");
        if (GoogleMode && view.Target is { } target) button.ContextMenu = ListMenu(target);
        button.Click += (_, _) =>
        {
            _taskViewKey = view.Key;
            RefreshTasksPage();
            TasksInput.Focus();
        };
        return button;
    }

    // ---------- Deleting a list ----------

    // What a list's ⋯ (and right-click) offers.
    private ContextMenu ListMenu(ListTarget target)
    {
        var delete = new MenuItem { Header = "Delete list…" };
        delete.Click += async (_, _) => await DeleteGoogleListAsync(target, askFirst: true);
        return new ContextMenu { Items = { delete } };
    }

    private void TasksListMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (_taskView?.Target is not { } target) return;
        var menu = ListMenu(target);
        menu.PlacementTarget = TasksListMenuButton;
        menu.Open(TasksListMenuButton);
    }

    private static string ListLabel(ListTarget target) => $"{GroupLabel(target.Group)} · {target.Name ?? GoogleGoalsSync.GeneralList}";

    // Deletes the list on Google (its tasks go with it, everywhere) and drops its tasks here.
    private async Task<bool> DeleteGoogleListAsync(ListTarget target, bool askFirst)
    {
        if (!GoogleMode || _googleSync == null) return false;
        var goals = _dailyGoals.Where(g => IsTargetOf(target, g)).ToList();
        if (askFirst)
        {
            var open = goals.Count(g => !g.Done);
            var what = open switch
            {
                0 => "Everything in it is deleted too",
                1 => "Its 1 open task is deleted too",
                _ => $"Its {open} open tasks are deleted too"
            };
            if (!await PromptWindow.Confirm(this, "Delete list",
                    $"Delete the list \"{ListLabel(target)}\" from Google Tasks?\n\n{what}, on your phone and everywhere else. This can't be undone.",
                    "Delete list"))
                return false;
        }

        await _googleLock.WaitAsync();
        try
        {
            await _googleSync.DeleteListAsync(target.Group, target.Name);
            SetSyncState(SyncState.Synced);
        }
        catch (Exception ex)
        {
            ReportGoogleError(ex);
            return false;
        }
        finally
        {
            _googleLock.Release();
        }

        // Gone on Google with the list; no per-task deletes to send.
        foreach (var goal in goals)
        {
            goal.PropertyChanged -= DailyGoal_PropertyChanged;
            _dailyGoals.Remove(goal);
        }
        if (_taskViewKey == "list:" + target.Key) _taskViewKey = "today";
        RefreshGoalScope();
        RefreshDailyProjectCombo();
        SaveDailyPlan();
        UpdateDailyProgress();
        return true;
    }

    private void TasksCompletedToggle_Click(object? sender, RoutedEventArgs e)
    {
        _showCompleted = !_showCompleted;
        RefreshTasksPage();
    }

    private void TasksInput_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = TasksInput.Text ?? "";
        TasksInputPlaceholder.IsVisible = text.Length == 0;
        if (text.IndexOfAny(new[] { '\n', '\r' }) < 0) return;
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        TasksInput.Text = "";
        AddToTaskView(lines.Select(l => l.TrimStart('-', '*', '•', ' ')));
    }

    private void TasksInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            TasksInput.Text = "";
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var text = (TasksInput.Text ?? "").Trim();
        if (text.Length == 0) return;
        TasksInput.Text = "";
        AddToTaskView(new[] { text });
    }

    private void AddToTaskView(IEnumerable<string> texts)
    {
        LoadDailyPlan(); // past midnight, it belongs to the new day
        var target = _taskView?.Target;
        foreach (var text in texts.Where(t => t.Length > 0))
        {
            AddDailyGoal(new DailyGoal
            {
                Text = text,
                Group = target?.Group ?? EditingGroup,
                ProjectId = target?.ProjectId,
                ProjectName = target?.Name,
                Starred = _taskViewKey == "important",
                Due = _pendingDue is DateTime d ? DueText.Key(d) : _taskViewKey == "planned" ? TodayKey : null,
                DueTime = _pendingTime?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            });
        }
        ClearPendingDue();
        UpdateDailyProgress();
    }

    private void TasksDue_Click(object? sender, RoutedEventArgs e) => OpenDuePicker(TasksDueButton, null);
}
