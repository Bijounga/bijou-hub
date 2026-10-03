using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BijouHub.Mac.Controls;
using BijouHub.Mac.Services;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Services.GoogleTasks;

namespace BijouHub.Mac;

// Today's goals — the same behaviour as Windows: type and press Enter, star to pin, check off,
// double-click to edit, drag to reorder, link to a project or list, notes for the day. With
// Google Tasks connected, goals live in "GROUP - Name" lists (one tab per group) and sync with
// the phone app, the web and the Windows BijouHub.
public partial class MainWindow
{
    private const string AllScope = "*";
    private const string EditingGroup = GoogleGoalsSync.EditingGroup;

    private DailyPlanStore _dailyStore = new();
    private readonly ObservableCollection<DailyGoal> _dailyGoals = new();
    private readonly ObservableCollection<DailyGoal> _visibleGoals = new();
    private DailyPlan? _today;
    private bool _loadingDailyPlan;
    private bool _relinking;
    private DispatcherTimer? _notesSaveTimer;
    private List<DailyGoal> _carryOverCandidates = new();
    private string _goalScope = EditingGroup;

    private sealed record ListTarget(string Group, string? Name, string? ProjectId, string Label)
    {
        public string Key => $"{Group}|{Name}".ToLowerInvariant();
        public override string ToString() => Label;
    }

    private void InitGoals()
    {
        _goalScope = _settingsStore.Load().GoalScope is { Length: > 0 } saved ? saved : EditingGroup;
        DailyGoalsList.ItemsSource = _visibleGoals;
        DailyGoalsList.AddHandler(DragDrop.DragOverEvent, GoalList_DragOver);
        DailyGoalsList.AddHandler(DragDrop.DropEvent, GoalList_Drop);

        _dailyGoals.CollectionChanged += (_, _) =>
        {
            if (_loadingDailyPlan || _applyingRemote) return;
            SaveDailyPlan();
        };

        _notesSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _notesSaveTimer.Tick += (_, _) =>
        {
            _notesSaveTimer.Stop();
            SaveDailyPlan();
        };
        UpdateAddGoalBar();
    }

    // ---------- Loading / saving ----------

    private void LoadDailyPlan()
    {
        var key = DailyPlanStore.Key(DateTime.Today);
        if (_today?.Date == key) return;

        _loadingDailyPlan = true;
        try
        {
            foreach (var goal in _dailyGoals) goal.PropertyChanged -= DailyGoal_PropertyChanged;
            _dailyGoals.Clear();
            _today = _dailyStore.LoadDay(DateTime.Today);
            foreach (var goal in _today.Goals)
            {
                goal.PropertyChanged += DailyGoal_PropertyChanged;
                _dailyGoals.Add(goal);
            }
            TakePlannedGoals();
            DailyNotesBox.Text = _today.Notes ?? "";
            _dayView = DayView.Today; // a new day opens on today
        }
        finally
        {
            _loadingDailyPlan = false;
        }
        SaveDailyPlan(); // keeps goals moved over from the plan
        RefreshGoalScope();
        RefreshCarryOver();
        UpdateDailyProgress();
    }

    private void SaveDailyPlan()
    {
        if (_today == null || _loadingDailyPlan) return;
        _today.Goals = _dailyGoals.ToList();
        _today.Notes = string.IsNullOrWhiteSpace(DailyNotesBox.Text) ? null : DailyNotesBox.Text;
        _dailyStore.SaveDay(_today);
        UpdateDailyProgress();
        BroadcastDeckGoals();
    }

    private void SaveDailyPlanNow()
    {
        if (_notesSaveTimer?.IsEnabled == true) SaveDailyPlan();
    }

    private void UpdateDailyProgress()
    {
        var visible = _dailyGoals.Where(InScope).ToList();
        var done = visible.Count(g => g.Done);
        DailyProgressText.Text = visible.Count == 0 ? "" : _dayView switch
        {
            DayView.Tomorrow => $"{visible.Count} planned",
            DayView.Upcoming => $"{visible.Count} upcoming",
            _ => done == visible.Count ? $"All {visible.Count} done" : $"{done} of {visible.Count} done"
        };
        RefreshDayTabs();
        RefreshTasksPage();
        DailyGoalsList.IsVisible = visible.Count > 0;
        BuildScopeTabs(ScopeGroups());
    }

    private void DailyGoal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applyingRemote || _relinking) return;
        if (e.PropertyName is nameof(DailyGoal.IsEditing) or nameof(DailyGoal.HasProject) or nameof(DailyGoal.ChipText)
            or nameof(DailyGoal.DueChip) or nameof(DailyGoal.DueOverdue)) return;
        if (e.PropertyName == nameof(DailyGoal.Starred)) PinStarredGoals();
        SaveDailyPlan();
        if (sender is DailyGoal goal) PushGoalChange(goal, e.PropertyName);
    }

    // Starred goals sit above the rest; within each group the user's drag order is kept.
    private void PinStarredGoals()
    {
        var desired = _dailyGoals.OrderBy(g => g.Starred ? 0 : 1).ToList();
        for (var i = 0; i < desired.Count; i++)
        {
            var current = _dailyGoals.IndexOf(desired[i]);
            if (current != i) _dailyGoals.Move(current, i);
        }
        SyncVisibleGoals();
    }

    // The list shows only the current tab's goals, in the master order.
    private void SyncVisibleGoals()
    {
        var desired = _dailyGoals.Where(InScope).ToList();
        if (desired.SequenceEqual(_visibleGoals)) return;
        _visibleGoals.Clear();
        foreach (var goal in desired) _visibleGoals.Add(goal);
    }

    private void AddDailyGoal(DailyGoal goal)
    {
        goal.ChipText = ChipFor(goal);
        goal.PropertyChanged += DailyGoal_PropertyChanged;
        _dailyGoals.Add(goal);
        if (goal.Starred) PinStarredGoals();
        SyncVisibleGoals();
        if (GoogleMode) QueueGoogleCreate(goal);
    }

    private void RemoveDailyGoal(DailyGoal goal)
    {
        goal.PropertyChanged -= DailyGoal_PropertyChanged;
        _dailyGoals.Remove(goal);
        SyncVisibleGoals();
        PushGoalDeleted(goal);
    }

    // ---------- The add bar ----------

    private void AddGoalBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<ComboBox>(true) != null) return;
        DailyGoalInput.Focus();
    }

    private void AddGoal_FocusChanged(object? sender, RoutedEventArgs e) => UpdateAddGoalBar();

    private void DailyGoalProjectCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((DailyGoalProjectCombo.SelectedItem as ComboBoxItem)?.Tag == NewListTag)
        {
            CreateListFromPicker();
            return;
        }
        UpdateAddGoalBar();
    }

    private void DailyGoalProjectCombo_DropDownClosed(object? sender, EventArgs e)
    {
        DailyGoalInput.Focus();
        UpdateAddGoalBar();
    }

    // The list picker stays out of the way until the bar is in use or something is picked.
    private void UpdateAddGoalBar()
    {
        var active = DailyGoalInput.IsFocused || DailyGoalProjectCombo.IsDropDownOpen || DailyGoalProjectCombo.IsFocused || DuePickerOpenForBar;
        UpdateAddGoalDue(active);
        DailyGoalProjectCombo.IsVisible = active || PickedTarget().Name != null;
        AddGoalIcon.Kind = active ? "check" : "add";
    }

    private void DailyGoalInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DailyGoalInput.Text = "";
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var text = (DailyGoalInput.Text ?? "").Trim();
        if (text.Length == 0) return;
        LoadDailyPlan(); // past midnight, the new goal belongs to the new day
        AddDailyGoal(NewGoal(text));
        DailyGoalInput.Text = "";
        ClearPendingDue();
    }

    private void DailyGoalInput_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = DailyGoalInput.Text ?? "";
        DailyGoalPlaceholder.IsVisible = text.Length == 0;
        // A pasted list arrives in one go — each line becomes its own goal.
        if (text.IndexOfAny(new[] { '\n', '\r' }) < 0) return;
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DailyGoalInput.Text = "";
        LoadDailyPlan();
        foreach (var line in lines) AddDailyGoal(NewGoal(line.TrimStart('-', '*', '•', ' ')));
        ClearPendingDue(); // a pasted list all gets the date that was picked
    }

    private DailyGoal NewGoal(string text)
    {
        var target = PickedTarget();
        var (due, time) = NewGoalDue();
        return new DailyGoal
        {
            Text = text,
            Group = target.Group,
            ProjectId = target.ProjectId,
            ProjectName = target.Name,
            Due = due,
            DueTime = time
        };
    }

    private static readonly object NewListTag = new();

    private void RefreshDailyProjectCombo(string? selectKey = null)
    {
        var keep = selectKey ?? PickedTarget().Key;
        var items = TargetsFor(_goalScope).Select(t => new ComboBoxItem { Content = t.Label, Tag = t }).ToList();
        if (GoogleMode) items.Add(new ComboBoxItem { Content = "+ New list…", Tag = NewListTag });
        DailyGoalProjectCombo.ItemsSource = items;
        DailyGoalProjectCombo.SelectedItem = items.FirstOrDefault(i => (i.Tag as ListTarget)?.Key == keep) ?? items.FirstOrDefault();
    }

    private ListTarget PickedTarget() =>
        (DailyGoalProjectCombo.SelectedItem as ComboBoxItem)?.Tag as ListTarget
        ?? TargetsFor(_goalScope).FirstOrDefault()
        ?? new ListTarget(EditingGroup, null, null, "No project");

    // ---------- Rows ----------

    private static DailyGoal? GoalOf(object? sender) => (sender as StyledElement)?.DataContext as DailyGoal;

    private void GoalStar_Click(object? sender, RoutedEventArgs e)
    {
        if (GoalOf(sender) is DailyGoal goal) goal.Starred = !goal.Starred;
    }

    private void GoalDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (GoalOf(sender) is DailyGoal goal) RemoveDailyGoal(goal);
    }

    private void GoalText_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (GoalOf(sender) is DailyGoal goal) goal.IsEditing = true;
    }

    // The inline editor takes focus (and its text) as it appears.
    private void GoalEditor_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || sender is not TextBox { IsVisible: true } box) return;
        box.Text = GoalOf(box)?.Text ?? "";
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    private void GoalEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || GoalOf(box) is not DailyGoal goal) return;
        if (e.Key == Key.Enter) CommitGoalEdit(box, goal);
        else if (e.Key == Key.Escape) goal.IsEditing = false;
        else return;
        e.Handled = true;
    }

    private void GoalEditor_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && GoalOf(box) is DailyGoal { IsEditing: true } goal) CommitGoalEdit(box, goal);
    }

    private static void CommitGoalEdit(TextBox box, DailyGoal goal)
    {
        var text = (box.Text ?? "").Trim();
        if (text.Length > 0) goal.Text = text;
        goal.IsEditing = false;
    }

    private void GoalMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu { DataContext: DailyGoal goal } menu) return;
        menu.Items.Clear();

        var edit = new MenuItem { Header = "Edit" };
        edit.Click += (_, _) => goal.IsEditing = true;
        menu.Items.Add(edit);

        var star = new MenuItem { Header = goal.Starred ? "Unstar" : "Star (pin to top)" };
        star.Click += (_, _) => goal.Starred = !goal.Starred;
        menu.Items.Add(star);

        var planned = goal.IsPlannedAfter(TodayKey);
        var move = new MenuItem { Header = planned ? "Move to today" : "Move to tomorrow" };
        move.Click += (_, _) => MoveGoalToDay(goal, !planned);
        menu.Items.Add(move);

        var due = new MenuItem { Header = "Date and time…" };
        due.Click += (_, _) => OpenDuePicker((menu.PlacementTarget as Control) ?? DailyGoalsList, goal);
        menu.Items.Add(due);

        var link = new MenuItem { Header = GoogleMode ? "Move to list" : "Link to project" };
        var scope = !GoogleMode ? EditingGroup : _goalScope == AllScope ? AllScope : GoogleGoalsSync.GroupOf(goal);
        foreach (var target in TargetsFor(scope))
        {
            var item = new MenuItem { Header = (IsTargetOf(target, goal) ? "✓  " : "     ") + target.Label };
            item.Click += (_, _) => RelinkGoal(goal, target);
            link.Items.Add(item);
        }
        menu.Items.Add(link);
        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => RemoveDailyGoal(goal);
        menu.Items.Add(delete);
    }

    // ---------- Drag to reorder ----------

    private const string GoalDragFormat = "bijouhub-goal";

    private async void GoalGrip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (GoalOf(sender) is not DailyGoal goal || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var data = new DataObject();
        data.Set(GoalDragFormat, goal.Id);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void GoalList_DragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.Data.Contains(GoalDragFormat) ? DragDropEffects.Move : DragDropEffects.None;

    private void GoalList_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get(GoalDragFormat) is not string id) return;
        var dragged = _dailyGoals.FirstOrDefault(g => g.Id == id);
        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true);
        if (dragged == null || item?.DataContext is not DailyGoal target || target == dragged) return;

        var below = e.GetPosition(item).Y > item.Bounds.Height / 2;
        _dailyGoals.Remove(dragged);
        var index = _dailyGoals.IndexOf(target) + (below ? 1 : 0);
        _dailyGoals.Insert(Math.Clamp(index, 0, _dailyGoals.Count), dragged);
        PinStarredGoals();
        SaveDailyPlan();
    }

    // ---------- Notes for the day ----------

    private void DailyNotesBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_loadingDailyPlan || _notesSaveTimer == null) return;
        _notesSaveTimer.Stop();
        _notesSaveTimer.Start();
    }

    private void DailyNotesBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (_notesSaveTimer?.IsEnabled != true) return;
        _notesSaveTimer.Stop();
        SaveDailyPlan();
    }

    // ---------- Carry over yesterday's unfinished goals (local goals only) ----------

    private void RefreshCarryOver()
    {
        _carryOverCandidates.Clear();
        if (!GoogleMode && _dayView == DayView.Today && _today is { CarryOverHandled: false } && _dailyStore.LatestUnfinishedBefore(DateTime.Today) is var (date, goals))
        {
            var carried = _dailyGoals.Select(g => g.CarriedFromId).ToHashSet();
            _carryOverCandidates = goals.Where(g => !carried.Contains(g.Id)).ToList();
            if (_carryOverCandidates.Count > 0)
            {
                var day = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var when = day == DateTime.Today.AddDays(-1) ? "yesterday" : day.ToString("dddd");
                var count = _carryOverCandidates.Count;
                CarryOverButton.Content = $"Carry over {count} unfinished goal{(count == 1 ? "" : "s")} from {when}";
            }
        }
        CarryOverRow.IsVisible = _carryOverCandidates.Count > 0;
    }

    private void CarryOver_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var old in _carryOverCandidates)
            AddDailyGoal(new DailyGoal { Text = old.Text, Starred = old.Starred, Group = old.Group, ProjectId = old.ProjectId, ProjectName = old.ProjectName, CarriedFromId = old.Id });
        DismissCarryOver_Click(sender, e);
    }

    private void DismissCarryOver_Click(object? sender, RoutedEventArgs e)
    {
        if (_today == null) return;
        _today.CarryOverHandled = true;
        SaveDailyPlan();
        _carryOverCandidates.Clear();
        CarryOverRow.IsVisible = false;
    }

    // ---------- Tabs (one per Google Tasks list group) ----------

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

    private void RefreshGoalScope()
    {
        var groups = ScopeGroups();
        if (_goalScope != AllScope && !groups.Contains(_goalScope)) _goalScope = EditingGroup;
        SyncVisibleGoals();
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
        GoalScopeTabs.IsVisible = GoogleMode;
        if (!GoalScopeTabs.IsVisible) return;

        foreach (var scope in groups.Append(AllScope))
        {
            var open = _dailyGoals.Count(g => !g.Done && InDay(g) && (scope == AllScope || GoogleGoalsSync.GroupOf(g) == scope));
            var selected = scope == _goalScope;
            var tab = new Button
            {
                Padding = new Thickness(12, 4),
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(14),
                Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 7,
                    Children =
                    {
                        new TextBlock { Text = GroupLabel(scope), FontSize = 12, FontWeight = selected ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal },
                        new TextBlock { Text = open.ToString(), FontSize = 11, Opacity = 0.7 }
                    }
                }
            };
            if (selected)
            {
                tab.BorderBrush = Brush("AccentBrush");
                tab.Foreground = Brush("AccentBrush");
            }
            ToolTip.SetTip(tab, scope == AllScope ? "Every list, in one place" : "Ctrl+Tab cycles tabs");
            Avalonia.Automation.AutomationProperties.SetName(tab, $"{GroupLabel(scope)} tab");
            tab.Click += (_, _) => SetGoalScope(scope);
            GoalScopeTabs.Children.Add(tab);
        }

        // "+": a new category (a Google Tasks list group, e.g. LIFE).
        var add = new Button { Padding = new Thickness(11, 3), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(14), Content = "+", FontSize = 14 };
        ToolTip.SetTip(add, "New category, like Life or Work");
        Avalonia.Automation.AutomationProperties.SetName(add, "New category");
        add.Click += async (_, _) => await AddCategoryAsync();
        GoalScopeTabs.Children.Add(add);
    }

    private async Task AddCategoryAsync()
    {
        var name = await PromptWindow.Ask(this, "New category",
            "Name it, like Life or Work. It's added to Google Tasks as a \"LIFE - General\"-style list, so it shows on your phone too.");
        if (name == null || _googleSync == null) return;

        var group = name.Trim().Trim('-').Trim().ToUpperInvariant();
        if (group.Length == 0) return;
        if (!ScopeGroups().Contains(group))
        {
            await _googleLock.WaitAsync();
            try
            {
                await _googleSync.CreateListAsync(GoogleGoalsSync.TitleFor(group, null));
                SetSyncState(SyncState.Synced);
            }
            catch (Exception ex)
            {
                ReportGoogleError(ex);
                return;
            }
            finally
            {
                _googleLock.Release();
            }
        }
        SetGoalScope(group);
        RefreshGoalScope();
        DailyGoalInput.Focus();
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+Tab / Ctrl+Shift+Tab cycle the goal tabs on Home (Cmd isn't free: it's tab switching on a Mac too).
        if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Control) && HomePanel.IsVisible && GoalScopeTabs.IsVisible)
        {
            var scopes = ScopeGroups().Append(AllScope).ToList();
            var index = Math.Max(0, scopes.IndexOf(_goalScope));
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            SetGoalScope(scopes[(index + step + scopes.Count) % scopes.Count]);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

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
                foreach (var name in _googleSync.ListNames(group)) yield return new ListTarget(group, name, null, prefix + name);
            }
            else
            {
                yield return new ListTarget(group, null, null, prefix + "General");
                foreach (var name in _googleSync.ListNames(group)) yield return new ListTarget(group, name, null, prefix + name);
            }
        }
    }

    private static bool IsTargetOf(ListTarget target, DailyGoal goal) =>
        target.Group == GoogleGoalsSync.GroupOf(goal) && string.Equals(target.Name, goal.ProjectName, StringComparison.OrdinalIgnoreCase);

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
        SyncVisibleGoals();
        SaveDailyPlan();
        if (GoogleMode) QueueGoogle(sync => sync.MoveToListAsync(goal));
    }

    private async void CreateListFromPicker()
    {
        var group = _goalScope;
        var message = group switch
        {
            AllScope => "Full name for the new list, e.g. \"STUDY - Exam prep\":",
            "" => "Name for the new list:",
            _ => $"Name for the new list (saved as \"{group} - name\"):"
        };
        var entered = await PromptWindow.Ask(this, "New list", message);
        if (entered == null || _googleSync == null)
        {
            RefreshDailyProjectCombo();
            return;
        }

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
