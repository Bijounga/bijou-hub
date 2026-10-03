using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using BijouHub.Models;
using BijouHub.Services.GoogleTasks;
using BijouHub.Views;

namespace BijouHub;

// Tabs over the goal list, one per Google Tasks list group ("EDITING - …", "STUDY - …",
// "LIFE - …", lists without a prefix as "Other") plus "All", so tasks kept for other apps are a
// click — or Ctrl+Tab — away. Local-only goals (no Google connection) have no tabs.
public partial class MainWindow
{
    private const string AllScope = "*";
    private const string EditingGroup = GoogleGoalsSync.EditingGroup;

    // Where a new or moved goal goes: a list group and the list inside it (null = its General
    // list, or "No project" for EDITING), plus the BijouHub project when the list is one.
    private sealed record ListTarget(string Group, string? Name, string? ProjectId, string Label)
    {
        public string Key => $"{Group}|{Name}".ToLowerInvariant();
    }

    private string _goalScope = EditingGroup;
    private bool _relinking;

    private void InitGoalScopes()
    {
        _goalScope = _settingsStore.Load().GoalScope is { Length: > 0 } saved ? saved : EditingGroup;
        CollectionViewSource.GetDefaultView(_dailyGoals).Filter = item => item is DailyGoal goal && InScope(goal);
    }

    private bool InScope(DailyGoal goal) =>
        InDay(goal) && (!GoogleMode || _goalScope == AllScope || GoogleGoalsSync.GroupOf(goal) == _goalScope);

    private IReadOnlyList<string> ScopeGroups()
    {
        if (!GoogleMode || _googleSync == null) return new[] { EditingGroup };
        var groups = _googleSync.Groups;
        return groups.Where(g => g == EditingGroup)
            .Concat(groups.Where(g => g.Length > 0 && g != EditingGroup).Order(StringComparer.Ordinal))
            .Concat(groups.Where(g => g.Length == 0))
            .ToList();
    }

    private static string GroupLabel(string group) => group switch
    {
        AllScope => "All",
        "" => "Other",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(group.ToLowerInvariant())
    };

    // Re-applies the filter, labels, tabs and picker after the scope or the goals changed.
    private void RefreshGoalScope()
    {
        var groups = ScopeGroups();
        if (_goalScope != AllScope && !groups.Contains(_goalScope)) _goalScope = EditingGroup;

        CollectionViewSource.GetDefaultView(_dailyGoals).Refresh();
        foreach (var goal in _dailyGoals) goal.ChipText = ChipFor(goal);
        RefreshDueChips();
        BuildScopeTabs(groups);
    }

    private string? ChipFor(DailyGoal goal)
    {
        if (!GoogleMode || _goalScope != AllScope) return goal.ProjectName;
        var label = GroupLabel(GoogleGoalsSync.GroupOf(goal));
        return goal.ProjectName == null ? label : $"{label} · {goal.ProjectName}";
    }

    private void BuildScopeTabs(IReadOnlyList<string> groups)
    {
        GoalScopeTabs.Children.Clear();
        if (!GoogleMode || groups.Count < 2)
        {
            GoalScopeTabs.Visibility = Visibility.Collapsed;
            return;
        }
        GoalScopeTabs.Visibility = Visibility.Visible;

        foreach (var scope in groups.Append(AllScope))
        {
            var open = _dailyGoals.Count(g => !g.Done && InDay(g) && (scope == AllScope || GoogleGoalsSync.GroupOf(g) == scope));
            GoalScopeTabs.Children.Add(BuildScopeTab(scope, open, scope == _goalScope));
        }
    }

    private Button BuildScopeTab(string scope, int open, bool selected)
    {
        var label = new TextBlock { Text = GroupLabel(scope), FontSize = 12, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
        label.SetResourceReference(TextBlock.ForegroundProperty, selected ? "AccentBrush" : "TextBrush");
        var count = new TextBlock { Text = open.ToString(), FontSize = 11, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, selected ? "AccentBrush" : "MutedTextBrush");

        var pill = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 4, 12, 4),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, count } }
        };
        pill.SetResourceReference(Border.BackgroundProperty, selected ? "CardHoverBrush" : "CardBrush");
        pill.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");

        var tab = new Button
        {
            Content = pill,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 6),
            Focusable = false,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) },
            ToolTip = scope == AllScope ? "Every list, in one place" : $"Lists named \"{(scope.Length == 0 ? "…" : GroupLabel(scope).ToUpperInvariant() + " - …")}\"  (Ctrl+Tab to cycle)"
        };
        System.Windows.Automation.AutomationProperties.SetName(tab, $"{GroupLabel(scope)} tab");
        tab.Click += (_, _) => SetGoalScope(scope);
        return tab;
    }

    private void SetGoalScope(string scope)
    {
        if (scope == _goalScope) return;
        _goalScope = scope;

        var settings = _settingsStore.Load();
        settings.GoalScope = scope;
        _settingsStore.Save(settings);

        RefreshGoalScope();
        RefreshDailyProjectCombo();
        UpdateDailyProgress();
    }

    private void CycleGoalScope(int direction)
    {
        var scopes = ScopeGroups().Append(AllScope).ToList();
        var index = scopes.IndexOf(_goalScope);
        SetGoalScope(scopes[((index < 0 ? 0 : index) + direction + scopes.Count) % scopes.Count]);
    }

    // The lists a goal can go to from a scope. With no Google connection: "No project" plus projects.
    private IEnumerable<ListTarget> TargetsFor(string scope)
    {
        if (!GoogleMode || _googleSync == null)
        {
            yield return new ListTarget(EditingGroup, null, null, "No project");
            foreach (var project in _projects) yield return new ListTarget(EditingGroup, project.Name, project.Id, project.Name);
            yield break;
        }

        var groups = scope == AllScope ? ScopeGroups() : new[] { scope };
        foreach (var group in groups)
        {
            var prefix = scope == AllScope ? GroupLabel(group) + " · " : "";
            if (group == EditingGroup)
            {
                yield return new ListTarget(group, null, null, scope == AllScope ? prefix + "General" : "No project");
                foreach (var project in _projects)
                    yield return new ListTarget(group, project.Name, project.Id, prefix + project.Name);
                foreach (var name in _googleSync.ListNames(group).Where(n => !_projects.Any(p => string.Equals(p.Name.Trim(), n, StringComparison.OrdinalIgnoreCase))))
                    yield return new ListTarget(group, name, null, prefix + name);
            }
            else if (group.Length == 0)
            {
                foreach (var name in _googleSync.ListNames(group))
                    yield return new ListTarget(group, name, null, prefix + name);
            }
            else
            {
                yield return new ListTarget(group, null, null, prefix + "General");
                foreach (var name in _googleSync.ListNames(group))
                    yield return new ListTarget(group, name, null, prefix + name);
            }
        }
    }

    // Moves a goal to another list as one change, so Google gets a single move.
    private void RelinkGoal(DailyGoal goal, ListTarget target)
    {
        _relinking = true;
        try
        {
            goal.Group = target.Group;
            goal.ProjectId = target.ProjectId;
            goal.ProjectName = target.Name;
        }
        finally
        {
            _relinking = false;
        }

        goal.ChipText = ChipFor(goal);
        CollectionViewSource.GetDefaultView(_dailyGoals).Refresh();
        SaveDailyPlan();
        if (GoogleMode) QueueGoogle(sync => sync.MoveToListAsync(goal));
    }

    private static bool IsTargetOf(ListTarget target, DailyGoal goal) =>
        target.Group == GoogleGoalsSync.GroupOf(goal) && string.Equals(target.Name, goal.ProjectName, StringComparison.OrdinalIgnoreCase);

    // "+ New list…": creates a list in the current tab's group (or any name on the All tab).
    private async void CreateGoogleListFromPicker()
    {
        var group = _goalScope;
        var prompt = group switch
        {
            AllScope => "Full name for the new list, e.g. \"STUDY - Exam prep\":",
            "" => "Name for the new list:",
            _ => $"Name for the new list (saved as \"{group} - name\"):"
        };
        var dialog = new TextPromptWindow("New list", prompt) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value) || _googleSync == null)
        {
            RefreshDailyProjectCombo();
            return;
        }

        var entered = dialog.Value.Trim();
        var title = group == AllScope ? entered : GoogleGoalsSync.TitleFor(group, entered);
        var (newGroup, newName) = GoogleGoalsSync.ParseTitle(title);

        await _googleLock.WaitAsync();
        try
        {
            await _googleSync.CreateListAsync(title);
            SetSyncState(SyncState.Synced);
        }
        catch (Exception ex)
        {
            ReportGoogleError(ex);
        }
        finally
        {
            _googleLock.Release();
        }

        RefreshGoalScope();
        var isGeneral = newGroup.Length > 0 && newName.Equals(GoogleGoalsSync.GeneralList, StringComparison.OrdinalIgnoreCase);
        RefreshDailyProjectCombo(new ListTarget(newGroup, isGeneral ? null : newName, null, "").Key);
    }
}
