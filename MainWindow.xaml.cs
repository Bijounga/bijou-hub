using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Views;

namespace BijouHub;

public partial class MainWindow : Window
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan BlockReminderInterval = TimeSpan.FromSeconds(20);

    private readonly ModeStore _modeStore = new();
    private readonly ProjectStore _projectStore = new();
    private readonly SessionLogService _logService = new();
    private readonly BlockWatcher _blockWatcher = new();
    private readonly KeybindStore _keybindStore = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _blockReminderTimer;
    private readonly Dictionary<string, DockPanel> _pendingBlockRows = new();
    private readonly Dictionary<string, BlockAlertWindow> _pendingAlertWindows = new();
    private List<WorkMode> _modes = new();
    private List<Project> _projects = new();
    private Project? _detailProject;

    private WorkMode? _activeMode;
    private Project? _activeProject;
    private Goal? _activeGoal;
    private int? _targetMinutes;
    private bool _budgetAlertShown;
    private BudgetAlertWindow? _budgetAlertWindow;
    private DateTime _sessionStart;
    private int _activeSeconds;
    private int _idleSeconds;
    private bool _isIdle;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Closing += (_, _) =>
        {
            if (ProjectDetailPanel.Visibility == Visibility.Visible) SaveFreeformNotes();
            _settingsStore.Save(new AppSettings { ZoomLevel = AppScaleTransform.ScaleX });
        };

        var settings = _settingsStore.Load();
        AppScaleTransform.ScaleX = settings.ZoomLevel;
        AppScaleTransform.ScaleY = settings.ZoomLevel;

        _modes = _modeStore.Load();
        ModesList.ItemsSource = _modes;

        _projects = _projectStore.Load();
        ProjectsList.ItemsSource = _projects;

        _blockWatcher.NewBlockedProcessDetected += OnNewBlockedProcessDetected;

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += TickTimer_Tick;

        _blockReminderTimer = new DispatcherTimer { Interval = BlockReminderInterval };
        _blockReminderTimer.Tick += (_, _) =>
        {
            if (_pendingBlockRows.Count > 0)
                System.Media.SystemSounds.Exclamation.Play();
        };

        InitNotesToolbar();
        ShowHome();
    }

    private static readonly int[] NoteFontSizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48 };

    private void InitNotesToolbar()
    {
        foreach (var size in NoteFontSizes)
            NotesFontSizeCombo.Items.Add(size);
        NotesFontSizeCombo.SelectedItem = 14;

        var systemFonts = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var family in systemFonts)
            NotesFontFamilyCombo.Items.Add(family);
        NotesFontFamilyCombo.SelectedItem = NotesFontFamilyCombo.Items.Cast<string>()
            .FirstOrDefault(f => f == "Consolas") ?? NotesFontFamilyCombo.Items[0];
    }

    private void NotesFontSizeUp_Click(object sender, RoutedEventArgs e) => StepFontSize(1);
    private void NotesFontSizeDown_Click(object sender, RoutedEventArgs e) => StepFontSize(-1);

    private void StepFontSize(int direction)
    {
        var current = NotesFontSizeCombo.SelectedItem is int size ? size : 14;
        var index = Array.IndexOf(NoteFontSizes, current);
        var nextIndex = index < 0
            ? Array.FindIndex(NoteFontSizes, s => s >= current)
            : Math.Clamp(index + direction, 0, NoteFontSizes.Length - 1);
        if (nextIndex < 0) nextIndex = direction > 0 ? NoteFontSizes.Length - 1 : 0;

        NotesFontSizeCombo.SelectedItem = NoteFontSizes[nextIndex];
    }

    // ---------- Sidebar / mode list ----------

    private void ModesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_activeMode != null) return; // don't let browsing interrupt an active session's view

        if (ModesList.SelectedItem is not WorkMode mode)
        {
            ShowEmptyState();
            return;
        }

        ProjectsList.SelectedItem = null;
        ShowModeDetail(mode);
    }

    private void NewMode_Click(object sender, RoutedEventArgs e)
    {
        var mode = new WorkMode();
        var editor = new ModeEditorWindow(mode) { Owner = this };
        if (editor.ShowDialog() != true) return;

        _modes.Add(mode);
        PersistAndRefreshModeList();
        ModesList.SelectedItem = mode;
    }

    private void EditMode_Click(object sender, RoutedEventArgs e)
    {
        if (ModesList.SelectedItem is not WorkMode mode) return;

        var editor = new ModeEditorWindow(mode) { Owner = this };
        if (editor.ShowDialog() != true) return;

        PersistAndRefreshModeList();
        ShowModeDetail(mode);
    }

    private void DeleteMode_Click(object sender, RoutedEventArgs e)
    {
        if (ModesList.SelectedItem is not WorkMode mode) return;

        var confirm = MessageBox.Show($"Delete mode \"{mode.Name}\"?", "Delete Mode",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _modes.Remove(mode);
        PersistAndRefreshModeList();
        ShowEmptyState();
    }

    private void PersistAndRefreshModeList()
    {
        _modeStore.Save(_modes);
        ModesList.ItemsSource = null;
        ModesList.ItemsSource = _modes;
    }

    // ---------- Sidebar / project list ----------

    private void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_activeMode != null) return;

        if (ProjectsList.SelectedItem is not Project project)
        {
            ShowEmptyState();
            return;
        }

        ModesList.SelectedItem = null;
        ShowProjectDetail(project);
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var project = new Project();
        var editor = new ProjectEditorWindow(project, _modes) { Owner = this };
        if (editor.ShowDialog() != true) return;

        _projects.Add(project);
        PersistAndRefreshProjectList();
        ProjectsList.SelectedItem = project;
    }

    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not Project project) return;

        var editor = new ProjectEditorWindow(project, _modes) { Owner = this };
        if (editor.ShowDialog() != true) return;

        PersistAndRefreshProjectList();
        ShowProjectDetail(project);
    }

    private void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not Project project) return;

        var confirm = MessageBox.Show($"Delete project \"{project.Name}\"?", "Delete Project",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _projects.Remove(project);
        PersistAndRefreshProjectList();
        ShowEmptyState();
    }

    private void PersistAndRefreshProjectList()
    {
        _projectStore.Save(_projects);
        ProjectsList.ItemsSource = null;
        ProjectsList.ItemsSource = _projects;
    }

    private void SelectProjectAndShowDetail(Project project)
    {
        ModesList.SelectedItem = null;
        ProjectsList.SelectedItem = project;
        ShowProjectDetail(project);
    }

    // ---------- Panel switching ----------

    private void HideAllPanels()
    {
        if (ProjectDetailPanel.Visibility == Visibility.Visible)
            SaveFreeformNotes();

        EmptyState.Visibility = Visibility.Collapsed;
        ModeDetailPanel.Visibility = Visibility.Collapsed;
        ActiveSessionPanel.Visibility = Visibility.Collapsed;
        ProjectDetailPanel.Visibility = Visibility.Collapsed;
        HomePanel.Visibility = Visibility.Collapsed;
    }

    private static void FadeIn(UIElement element)
    {
        element.Opacity = 0;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void ShowEmptyState()
    {
        HideAllPanels();
        EmptyState.Visibility = Visibility.Visible;
        FadeIn(EmptyState);
    }

    private void ShowModeDetail(WorkMode mode)
    {
        HideAllPanels();
        ModeDetailPanel.Visibility = Visibility.Visible;
        FadeIn(ModeDetailPanel);

        ModeDetailName.Text = mode.Name;
        ModeDetailLaunchItems.ItemsSource = mode.LaunchItems;
        ModeDetailBlockItems.ItemsSource = mode.BlockItems;
    }

    private void ShowProjectDetail(Project project)
    {
        HideAllPanels();
        ProjectDetailPanel.Visibility = Visibility.Visible;
        FadeIn(ProjectDetailPanel);
        _detailProject = project;

        ProjectDetailName.Text = project.Name;
        ProjectProgressFill.Width = 320 * Math.Clamp(project.Completion, 0, 1);
        ProjectProgressLabel.Text = $"{project.CompletionPercentText} complete — {project.NextGoalSummary}";

        var totalSeconds = _logService.GetForProject(project.Id).Sum(s => s.ActiveSeconds);
        ProjectTimeSpentText.Text = $"Time spent: {FormatSpan(totalSeconds)}";

        ProjectGoalsDisplay.ItemsSource = project.Goals.Count > 0 ? project.Goals : null;

        ProjectNotesDisplay.ItemsSource = project.Notes.Count > 0
            ? project.Notes.OrderByDescending(n => n.Timestamp)
                .Select(n => $"{n.Timestamp:MMM d, HH:mm} — {n.Text}").ToList()
            : new List<string> { "No notes yet." };

        LoadFreeformNotes(project);
        ApplyNoteKeybinds();
    }

    private void LoadFreeformNotes(Project project)
    {
        NotesRichBox.Document = new FlowDocument();
        if (string.IsNullOrEmpty(project.FreeformNotesXaml)) return;

        try
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(project.FreeformNotesXaml));
            var range = new TextRange(NotesRichBox.Document.ContentStart, NotesRichBox.Document.ContentEnd);
            range.Load(ms, DataFormats.Xaml);
        }
        catch
        {
            // Corrupt or incompatible saved content; start fresh rather than crash.
        }
    }

    private void SaveFreeformNotes()
    {
        if (_detailProject == null) return;

        var range = new TextRange(NotesRichBox.Document.ContentStart, NotesRichBox.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Xaml);
        _detailProject.FreeformNotesXaml = Encoding.UTF8.GetString(ms.ToArray());
        _projectStore.Save(_projects);
    }

    private void ShowHome()
    {
        HideAllPanels();
        HomePanel.Visibility = Visibility.Visible;
        FadeIn(HomePanel);

        HomeGreetingText.Text = Greetings.Random();

        var todaySeconds = _logService.GetTodayTotalSeconds();
        HomeTodayText.Text = FormatSpan(todaySeconds);

        var last7 = _logService.GetLastNDaysTotals(7);
        HomeWeekText.Text = "Last 7 days: " + string.Join("   ", last7.Select(kv => $"{kv.Key:ddd} {FormatSpan(kv.Value)}"));

        HomeProjectsList.ItemsSource = _projects;
    }

    private void HomeHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (_activeMode != null) return; // don't interrupt an active session's view
        ModesList.SelectedItem = null;
        ProjectsList.SelectedItem = null;
        ShowHome();
    }

    private void HomeProjectRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (_activeMode != null) return;
        if (sender is FrameworkElement { DataContext: Project project })
            SelectProjectAndShowDetail(project);
    }

    private static string FormatSpan(int totalSeconds)
    {
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m";
    }

    // ---------- Launch / session (Mode only, no project) ----------

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (ModesList.SelectedItem is not WorkMode mode) return;
        await BeginSession(mode, null, null, null);
    }

    // ---------- Start session from a Project ----------

    private async void StartProjectSession_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not Project project) return;

        var dlg = new StartSessionWindow(project, _modes) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        await BeginSession(dlg.SelectedMode, project, dlg.SelectedGoal, dlg.TargetMinutes);
    }

    private async Task BeginSession(WorkMode? mode, Project? project, Goal? goal, int? targetMinutes)
    {
        if (mode != null)
        {
            var failures = await ModeLauncherService.LaunchAsync(mode);
            if (failures.Count > 0)
            {
                MessageBox.Show(
                    "Some items in this mode failed to launch:\n\n" + string.Join("\n", failures),
                    "Launch Mode", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        _activeMode = mode;
        _activeProject = project;
        _activeGoal = goal;
        _targetMinutes = targetMinutes;
        _budgetAlertShown = false;
        _sessionStart = DateTime.Now;
        _activeSeconds = 0;
        _idleSeconds = 0;
        _isIdle = false;

        BlockNotifications.Children.Clear();
        _pendingBlockRows.Clear();
        CloseAllAlertWindows();
        _blockReminderTimer.Stop();

        ActiveModeName.Text = project?.Name ?? mode?.Name ?? "Session";
        var contextParts = new List<string>();
        if (project != null && mode != null) contextParts.Add($"via {mode.Name}");
        if (goal != null) contextParts.Add($"working on: {goal.Name}");
        if (targetMinutes is int tm) contextParts.Add($"budget: {tm} min");
        ActiveContextText.Text = string.Join("  •  ", contextParts);
        ActiveContextText.Visibility = contextParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ActiveStatus.Text = "Active";
        TimerDisplay.Text = "00:00:00";

        HideAllPanels();
        ActiveSessionPanel.Visibility = Visibility.Visible;
        FadeIn(ActiveSessionPanel);

        if (mode != null)
            _blockWatcher.Start(mode);
        _tickTimer.Start();
    }

    private void TickTimer_Tick(object? sender, EventArgs e)
    {
        var idleTime = IdleTimeService.GetIdleTime();

        if (idleTime >= IdleThreshold)
        {
            _idleSeconds++;
            if (!_isIdle)
            {
                _isIdle = true;
                ActiveStatus.Text = "Idle — timer paused";
            }
        }
        else
        {
            _activeSeconds++;
            if (_isIdle)
            {
                _isIdle = false;
                ActiveStatus.Text = "Active";
            }
        }

        TimerDisplay.Text = TimeSpan.FromSeconds(_activeSeconds).ToString(@"hh\:mm\:ss");

        if (_targetMinutes is int target && !_budgetAlertShown && _activeSeconds >= target * 60)
        {
            _budgetAlertShown = true;
            ShowBudgetAlert(target);
        }
    }

    private void ShowBudgetAlert(int targetMinutes)
    {
        System.Media.SystemSounds.Exclamation.Play();

        _budgetAlertWindow?.Close();
        var alert = new BudgetAlertWindow(_activeProject?.Name ?? _activeMode?.Name ?? "session", targetMinutes);
        var workArea = SystemParameters.WorkArea;
        alert.Left = workArea.Right - alert.Width - 16;
        alert.Top = workArea.Bottom - alert.Height - 16;

        alert.Extended += () =>
        {
            _targetMinutes = (_targetMinutes ?? targetMinutes) + 15;
            _budgetAlertShown = false;
            alert.Close();
        };
        alert.FinishRequested += () =>
        {
            alert.Close();
            FinishSession_Click(this, new RoutedEventArgs());
        };

        _budgetAlertWindow = alert;
        alert.Show();
    }

    private void OnNewBlockedProcessDetected(string processName, int pid)
    {
        if (_pendingBlockRows.ContainsKey(processName))
        {
            System.Media.SystemSounds.Exclamation.Play();
            return;
        }

        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(new TextBlock
        {
            Text = $"{processName} was opened — blocked in this mode",
            VerticalAlignment = VerticalAlignment.Center
        });

        var allowBtn = new Button { Content = "Allow 10 min", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        var closeBtn = new Button { Content = "Close it", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        DockPanel.SetDock(closeBtn, Dock.Right);
        DockPanel.SetDock(allowBtn, Dock.Right);
        allowBtn.Click += (_, _) => AllowBlock(processName);
        closeBtn.Click += (_, _) => CloseBlock(processName);
        row.Children.Add(closeBtn);
        row.Children.Add(allowBtn);
        BlockNotifications.Children.Add(row);
        _pendingBlockRows[processName] = row;

        var alert = new BlockAlertWindow(processName);
        alert.Allowed += () => AllowBlock(processName);
        alert.Dismissed += () => CloseBlock(processName);
        _pendingAlertWindows[processName] = alert;
        RepositionAlerts();
        alert.Show();

        System.Media.SystemSounds.Exclamation.Play();
        _blockReminderTimer.Start();
    }

    private void AllowBlock(string processName)
    {
        _blockWatcher.AllowTemporarily(processName, TimeSpan.FromMinutes(10));
        ClearPendingBlock(processName);
    }

    private void CloseBlock(string processName)
    {
        BlockWatcher.CloseProcess(processName);
        ClearPendingBlock(processName);
    }

    private void ClearPendingBlock(string processName)
    {
        if (_pendingBlockRows.Remove(processName, out var row))
            BlockNotifications.Children.Remove(row);

        if (_pendingAlertWindows.Remove(processName, out var alert))
            alert.Close();

        RepositionAlerts();

        if (_pendingBlockRows.Count == 0)
            _blockReminderTimer.Stop();
    }

    private void CloseAllAlertWindows()
    {
        foreach (var alert in _pendingAlertWindows.Values)
            alert.Close();
        _pendingAlertWindows.Clear();

        _budgetAlertWindow?.Close();
        _budgetAlertWindow = null;
    }

    private void RepositionAlerts()
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 16;
        const double gap = 10;
        var bottom = workArea.Bottom - margin;

        foreach (var alert in _pendingAlertWindows.Values)
        {
            alert.Left = workArea.Right - alert.Width - margin;
            alert.Top = bottom - alert.Height;
            bottom -= alert.Height + gap;
        }
    }

    private void FinishSession_Click(object sender, RoutedEventArgs e)
    {
        if (_activeMode == null && _activeProject == null) return;

        _tickTimer.Stop();
        _blockWatcher.Stop();
        _blockReminderTimer.Stop();
        _pendingBlockRows.Clear();
        CloseAllAlertWindows();

        string? note = null;
        var finishedProject = _activeProject;
        if (finishedProject != null)
        {
            var noteDlg = new FinishNoteWindow { Owner = this };
            if (noteDlg.ShowDialog() == true)
                note = noteDlg.Note;

            if (!string.IsNullOrEmpty(note))
            {
                finishedProject.Notes.Add(new ProjectNote { Text = note });
                _projectStore.Save(_projects);
            }
        }

        _logService.InsertSession(new SessionRecord
        {
            ModeName = _activeMode?.Name ?? "",
            StartTime = _sessionStart,
            EndTime = DateTime.Now,
            ActiveSeconds = _activeSeconds,
            IdleSeconds = _idleSeconds,
            ProjectId = finishedProject?.Id,
            ProjectName = finishedProject?.Name,
            GoalId = _activeGoal?.Id,
            GoalName = _activeGoal?.Name,
            Note = note
        });

        var finishedMode = _activeMode;
        _activeMode = null;
        _activeProject = null;
        _activeGoal = null;
        _targetMinutes = null;

        if (finishedProject != null)
        {
            PersistAndRefreshProjectList();
            SelectProjectAndShowDetail(finishedProject);
        }
        else if (ModesList.SelectedItem is WorkMode selected && selected == finishedMode)
        {
            ShowModeDetail(finishedMode);
        }
        else
        {
            ShowEmptyState();
        }
    }

    // ---------- Session log ----------

    private void SessionLog_Click(object sender, RoutedEventArgs e)
    {
        var win = new SessionLogWindow(_logService) { Owner = this };
        win.ShowDialog();
    }

    // ---------- Zoom ----------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        if (e.Key is Key.OemPlus or Key.Add)
        {
            SetZoom(AppScaleTransform.ScaleX + 0.1);
            e.Handled = true;
        }
        else if (e.Key is Key.OemMinus or Key.Subtract)
        {
            SetZoom(AppScaleTransform.ScaleX - 0.1);
            e.Handled = true;
        }
        else if (e.Key == Key.D0)
        {
            SetZoom(1.0);
            e.Handled = true;
        }
    }

    private void SetZoom(double scale)
    {
        scale = Math.Clamp(scale, 0.7, 1.6);
        AppScaleTransform.ScaleX = scale;
        AppScaleTransform.ScaleY = scale;
    }

    // ---------- Freeform notes (rich text) ----------

    private void NotesBold_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleBold.Execute(null, NotesRichBox);
    private void NotesItalic_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleItalic.Execute(null, NotesRichBox);
    private void NotesBullet_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleBullets.Execute(null, NotesRichBox);

    private void NotesHeading_Click(object sender, RoutedEventArgs e)
    {
        var selection = NotesRichBox.Selection;
        var target = !selection.IsEmpty
            ? (System.Windows.Documents.TextRange)selection
            : GetCaretParagraphRange();
        if (target == null) return;

        bool isHeading = Equals(target.GetPropertyValue(TextElement.FontSizeProperty), 20.0);
        target.ApplyPropertyValue(TextElement.FontSizeProperty, isHeading ? 14.0 : 20.0);
        target.ApplyPropertyValue(TextElement.FontWeightProperty, isHeading ? FontWeights.Normal : FontWeights.Bold);
        NotesRichBox.Focus();
    }

    private System.Windows.Documents.TextRange? GetCaretParagraphRange()
    {
        var para = NotesRichBox.CaretPosition.Paragraph;
        return para == null ? null : new System.Windows.Documents.TextRange(para.ContentStart, para.ContentEnd);
    }

    private void NotesChecklist_Click(object sender, RoutedEventArgs e)
    {
        var para = NotesRichBox.CaretPosition.Paragraph ?? NotesRichBox.Document.Blocks.LastBlock as Paragraph;
        if (para == null) return;

        var lineStart = para.ContentStart;
        var peekEnd = lineStart.GetPositionAtOffset(2) ?? para.ContentEnd;
        var prefix = new System.Windows.Documents.TextRange(lineStart, peekEnd).Text;

        if (prefix.StartsWith("☐"))
        {
            var oneChar = lineStart.GetPositionAtOffset(1) ?? peekEnd;
            new System.Windows.Documents.TextRange(lineStart, oneChar).Text = "☑";
        }
        else if (prefix.StartsWith("☑"))
        {
            var twoChars = lineStart.GetPositionAtOffset(2) ?? para.ContentEnd;
            new System.Windows.Documents.TextRange(lineStart, twoChars).Text = "";
        }
        else
        {
            new System.Windows.Documents.TextRange(lineStart, lineStart).Text = "☐ ";
        }
        NotesRichBox.Focus();
    }

    private void NotesFontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NotesFontSizeCombo.SelectedItem is not int size) return;
        NotesRichBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, (double)size);
        NotesRichBox.Focus();
    }

    private void NotesFontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NotesFontFamilyCombo.SelectedItem is not string family) return;
        NotesRichBox.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new System.Windows.Media.FontFamily(family));
        NotesRichBox.Focus();
    }

    private void OpenKeybinds_Click(object sender, RoutedEventArgs e)
    {
        var win = new KeybindsWindow(_keybindStore) { Owner = this };
        win.KeybindsChanged += ApplyNoteKeybinds;
        win.ShowDialog();
    }

    private void ApplyNoteKeybinds()
    {
        NotesRichBox.InputBindings.Clear();
        NotesRichBox.CommandBindings.Clear();

        AddBind("Bold", (s, _) => NotesBold_Click(s!, new RoutedEventArgs()));
        AddBind("Italic", (s, _) => NotesItalic_Click(s!, new RoutedEventArgs()));
        AddBind("Heading", (s, _) => NotesHeading_Click(s!, new RoutedEventArgs()));
        AddBind("BulletList", (s, _) => NotesBullet_Click(s!, new RoutedEventArgs()));
        AddBind("Checklist", (s, _) => NotesChecklist_Click(s!, new RoutedEventArgs()));

        void AddBind(string action, ExecutedRoutedEventHandler handler)
        {
            var gesture = KeybindStore.ParseGesture(_keybindStore.Get(action));
            if (gesture == null) return;
            var cmd = new RoutedCommand();
            NotesRichBox.CommandBindings.Add(new CommandBinding(cmd, handler));
            NotesRichBox.InputBindings.Add(new KeyBinding(cmd, gesture));
        }
    }
}
