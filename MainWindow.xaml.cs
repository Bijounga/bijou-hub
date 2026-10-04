using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Services.GoogleTasks;
using BijouHub.Views;

namespace BijouHub;

public partial class MainWindow : Window
{
    // BIJOUHUB_IDLE_MINUTES lets automated tests run without someone at the keyboard.
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(
        double.TryParse(Environment.GetEnvironmentVariable("BIJOUHUB_IDLE_MINUTES"), out var idleMinutes) ? idleMinutes : 3);
    private static readonly TimeSpan BlockReminderInterval = TimeSpan.FromSeconds(20);

    private readonly ModeStore _modeStore = new();
    private readonly ProjectStore _projectStore = new();
    private readonly SessionLogService _logService = new();
    private readonly BlockWatcher _blockWatcher = new();
    private readonly KeybindStore _keybindStore = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly QuickLaunchStore _quickLaunchStore = new();
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _blockReminderTimer;
    private readonly Dictionary<string, DockPanel> _pendingBlockRows = new();
    private readonly Dictionary<string, BlockAlertWindow> _pendingAlertWindows = new();
    private List<WorkMode> _modes = new();
    private List<Project> _projects = new();
    private List<QuickLaunchApp> _quickLaunchApps = new();
    private Project? _detailProject;

    private WorkMode? _activeMode;
    private Project? _activeProject;
    private Goal? _activeGoal;
    private int? _targetMinutes;
    private bool _countDownMode;
    private bool _budgetAlertShown;
    private BudgetAlertWindow? _budgetAlertWindow;
    private DateTime _sessionStart;
    private int _activeSeconds;
    private int _idleSeconds;
    private bool _isIdle;
    private bool _notesVisible = true;
    private TimerPopoutWindow? _timerPopout;
    private bool _paused;
    private bool _sessionStarting;

    // Stream Deck key (the plugin's per-key id) that started the running session, if any —
    // tells the deck which key should draw the live countdown.
    private string? _deckKeyId;
    private readonly StreamDeckBridge _deckBridge;

    private bool IsSessionActive => _activeMode != null || _activeProject != null;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        RefreshThemeChrome();
        _deckBridge = new StreamDeckBridge(command => Dispatcher.InvokeAsync(command).Task, HandleDeckCommand);
        Closing += (_, e) =>
        {
            // Kept running for the Stream Deck: the close button hides it to the tray.
            if (_keepRunning && !App.Quitting)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            _tray?.Dispose();

            if (NotesPanel.Visibility == Visibility.Visible) SaveFreeformNotes();
            _timerPopout?.Close();
            _deckBridge.Dispose();
            if (_notesSaveTimer?.IsEnabled == true) SaveDailyPlan(); // notes typed in the last second
            _dailyStore.Flush(); // goal saves are written in the background
            var currentSettings = _settingsStore.Load();
            currentSettings.ZoomLevel = AppScaleTransform.ScaleX;
            _settingsStore.Save(currentSettings);

            if (IsSessionActive)
                FinalizeSessionSilently();
        };

        RecoverInterruptedSessionIfAny();
        DoNotDisturb.Restore(); // left on by a session that didn't end cleanly

        var settings = _settingsStore.Load();
        _dailyTargetMinutes = settings.DailyTargetMinutes;
        AppScaleTransform.ScaleX = settings.ZoomLevel;
        AppScaleTransform.ScaleY = settings.ZoomLevel;

        _modes = _modeStore.Load();
        ModesList.ItemsSource = _modes;

        _projects = _projectStore.Load();
        ProjectsList.ItemsSource = _projects;

        _blockWatcher.NewBlockedProcessDetected += OnNewBlockedProcessDetected;
        _blockWatcher.HardBlockedProcessClosed += OnHardBlockedProcessClosed;

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += TickTimer_Tick;

        _blockReminderTimer = new DispatcherTimer { Interval = BlockReminderInterval };
        _blockReminderTimer.Tick += (_, _) =>
        {
            if (_pendingBlockRows.Count > 0)
                System.Media.SystemSounds.Exclamation.Play();
        };

        _deckBridge.Start();

        _quickLaunchApps = _quickLaunchStore.Load();
        foreach (var app in _quickLaunchApps)
            app.Icon = IconExtractor.GetIcon(app.Path);

        InitNotesToolbar();
        InitDailyGoals();
        InitDue();
        InitTasksPage();
        InitGoogleTasks();
        InitGoalScopes();
        VersionText.Text = "v" + AppVersion;
        RefreshQuickLaunchPanel();
        UpdateSyncFolderButtonLabel();
        UpdateStartupButtonLabel();
        InitTray();
        ShowHome();
        _ = CheckForUpdateAsync();
        // Kept running in the tray, BijouHub can go days without a restart: look again every few hours.
        var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        updateTimer.Tick += (_, _) => { if (_pendingUpdate == null) _ = CheckForUpdateAsync(); };
        updateTimer.Start();
    }

    // "1.13.0" — the informational version minus the commit hash the SDK appends.
    private static string AppVersion =>
        (System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "")
        .Split('+')[0];

    private UpdateInfo? _pendingUpdate;

    private async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        var update = await UpdateService.CheckForUpdateAsync();
        if (update == null) return null;

        _pendingUpdate = update;
        if (!IsVisible) _tray?.ShowBalloon("BijouHub update", $"Version {update.Version} is ready. Open BijouHub and click Update.");
        UpdateButton.Content = $"⬆ Update to v{update.Version}";
        UpdateButton.ToolTip = "A new version of BijouHub is available. Click to download and restart.";
        UpdateButton.Visibility = Visibility.Visible;
        return update;
    }

    private bool _checkingForUpdates;

    // Clicking the version number checks for an update right now.
    private async void VersionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingForUpdates) return;
        _checkingForUpdates = true;
        VersionText.Text = "checking…";
        var update = await CheckForUpdateAsync();
        VersionText.Text = update != null ? "update ready ↓" : "up to date ✓";
        await Task.Delay(2500);
        VersionText.Text = "v" + AppVersion;
        _checkingForUpdates = false;
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;

        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "Downloading update...";
        try
        {
            await UpdateService.DownloadAndApplyAsync(_pendingUpdate.DownloadUrl);
        }
        catch (Exception ex)
        {
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = $"⬆ Update to v{_pendingUpdate.Version}";
            MessageBox.Show($"Couldn't apply the update: {ex.Message}", "Update Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetupAnimatedProgressFill()
    {
        var stripes = new System.Windows.Media.GeometryDrawing
        {
            Brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)Application.Current.Resources["HazardColor"]),
            Geometry = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 10, 10))
        };
        var hatch = new System.Windows.Media.GeometryDrawing
        {
            Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0, 0, 0)),
            Geometry = System.Windows.Media.Geometry.Parse("M0,10 L10,0 L10,3 L3,10 Z M0,3 L3,0 L0,0 Z")
        };
        var group = new System.Windows.Media.DrawingGroup();
        group.Children.Add(stripes);
        group.Children.Add(hatch);

        var scroll = new System.Windows.Media.TranslateTransform();
        var brush = new System.Windows.Media.DrawingBrush(group)
        {
            TileMode = System.Windows.Media.TileMode.Tile,
            Viewport = new Rect(0, 0, 10, 10),
            ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute,
            Stretch = System.Windows.Media.Stretch.None,
            Transform = scroll
        };

        ProjectProgressFill.Background = brush;

        var anim = new DoubleAnimation(0, 10, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever };
        scroll.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
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
        if (ModesList.SelectedItem is not WorkMode mode)
        {
            if (!IsSessionActive) ShowEmptyState();
            return;
        }

        ProjectsList.SelectedItem = null;

        if (mode == _activeMode)
        {
            ShowActiveSessionPanel();
            return;
        }

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
        ModesList.SelectedItem = mode;
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
        if (ProjectsList.SelectedItem is not Project project)
        {
            if (!IsSessionActive) ShowEmptyState();
            return;
        }

        ModesList.SelectedItem = null;

        if (project == _activeProject)
        {
            ShowActiveSessionPanel();
            return;
        }

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
        ProjectsList.SelectedItem = project;
    }

    private void LogTime_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not Project project) return;

        var dlg = new LogTimeWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var end = DateTime.Now;
        var start = end.AddMinutes(-dlg.TotalMinutes);

        _logService.InsertSession(new SessionRecord
        {
            ModeName = "Manual",
            StartTime = start,
            EndTime = end,
            ActiveSeconds = dlg.TotalMinutes * 60,
            IdleSeconds = 0,
            ProjectId = project.Id,
            ProjectName = project.Name,
            Note = dlg.Note
        });
        InvalidateTodayLogged();

        if (!string.IsNullOrEmpty(dlg.Note))
        {
            project.Notes.Add(new ProjectNote { Timestamp = end, Text = dlg.Note });
            _projectStore.Save(_projects);
        }

        SelectProjectAndShowDetail(project);
    }

    private async void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not Project project) return;

        // With Google Tasks, the project has a list there too ("EDITING - name"): offer to delete it with it.
        var deleteList = false;
        if (GoogleMode && _googleSync?.HasList(EditingGroup, project.Name) == true)
        {
            var open = _dailyGoals.Count(g => !g.Done && GoogleGoalsSync.GroupOf(g) == EditingGroup
                                              && string.Equals(g.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase));
            var tasks = open switch { 0 => "", 1 => " (1 open task)", _ => $" ({open} open tasks)" };
            var choice = ChoiceWindow.Ask(this, "Delete project",
                $"Delete \"{project.Name}\"?\n\nIt also has a list in Google Tasks, EDITING - {project.Name}{tasks}. " +
                "Delete that too, or keep it (it stays under Editing on the Tasks page and on your phone)?",
                "Delete project only", "Delete project and its list");
            if (choice < 0) return;
            deleteList = choice == 1;
        }
        else
        {
            var confirm = MessageBox.Show($"Delete project \"{project.Name}\"?", "Delete Project",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        _projects.Remove(project);
        PersistAndRefreshProjectList();
        ShowEmptyState();
        if (deleteList) await DeleteGoogleListAsync(new ListTarget(EditingGroup, project.Name, project.Id, project.Name), askFirst: false);
    }

    private void PersistAndRefreshProjectList()
    {
        _boardBuiltAt = DateTime.MinValue;
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
        if (NotesPanel.Visibility == Visibility.Visible)
            SaveFreeformNotes();

        EmptyState.Visibility = Visibility.Collapsed;
        TasksPanel.Visibility = Visibility.Collapsed;
        ModeDetailPanel.Visibility = Visibility.Collapsed;
        ActiveSessionPanel.Visibility = Visibility.Collapsed;
        ProjectDetailPanel.Visibility = Visibility.Collapsed;
        HomePanel.Visibility = Visibility.Collapsed;

        ActiveSessionBanner.Visibility = IsSessionActive ? Visibility.Visible : Visibility.Collapsed;
        if (IsSessionActive)
            ActiveSessionBannerText.Text = TimerDisplay.Text;
    }

    private void UpdateNotesPanelVisibility()
    {
        var show = _notesVisible && _detailProject != null;
        NotesPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        var label = _notesVisible ? "Hide notes" : "Show notes";
        if (NotesToggleButton != null) NotesToggleButton.ToolTip = label;
        if (ProjectNotesToggleButton != null) ProjectNotesToggleButton.ToolTip = label;
    }

    private void NotesToggle_Click(object sender, RoutedEventArgs e)
    {
        _notesVisible = !_notesVisible;
        UpdateNotesPanelVisibility();
    }

    private void ActiveSessionBanner_Click(object sender, MouseButtonEventArgs e)
    {
        ShowActiveSessionPanel();
    }

    private void PopoutToggle_Click(object sender, RoutedEventArgs e) => SetTimerPopout(_timerPopout == null);

    // Pops the mini timer out (always on top, for working in another app) or docks it again.
    private void SetTimerPopout(bool open)
    {
        if (!open)
        {
            _timerPopout?.Close();
            return;
        }
        if (_timerPopout != null) return;

        var popout = new TimerPopoutWindow();
        popout.UpdateDisplay(_activeProject?.Name ?? _activeMode?.Name ?? "Session", TimerDisplay.Text, ActiveStatus.Text);
        popout.DockRequested += () => popout.Close();
        popout.FinishRequested += () =>
        {
            popout.Close();
            FinishSession_Click(this, new RoutedEventArgs());
        };
        popout.Closed += (_, _) =>
        {
            _timerPopout = null;
            BroadcastDeckState();
            SetIconButton(PopoutToggleButton, "\uE8A7", "Pop out the timer");
        };

        _timerPopout = popout;
        SetIconButton(PopoutToggleButton, "\uE73F", "Dock the timer");
        popout.Show();
        BroadcastDeckState();
    }

    private void ShowActiveSessionPanel()
    {
        HideAllPanels();
        ActiveSessionPanel.Visibility = Visibility.Visible;
        FadeIn(ActiveSessionPanel);
        ActiveSessionBanner.Visibility = Visibility.Collapsed;

        _detailProject = _activeProject;
        if (_activeProject != null)
            LoadFreeformNotes(_activeProject);
        UpdateNotesPanelVisibility();
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

    private static void TypewriterReveal(TextBlock target, string fullText, int msPerChar = 16, Action? onComplete = null)
    {
        target.Text = "";
        if (string.IsNullOrEmpty(fullText))
        {
            onComplete?.Invoke();
            return;
        }

        var i = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(msPerChar) };
        timer.Tick += (_, _) =>
        {
            i++;
            target.Text = fullText[..Math.Min(i, fullText.Length)];
            if (i >= fullText.Length)
            {
                timer.Stop();
                onComplete?.Invoke();
            }
        };
        timer.Start();
    }

    private void ShowEmptyState()
    {
        HideAllPanels();
        EmptyState.Visibility = Visibility.Visible;
        FadeIn(EmptyState);

        _detailProject = null;
        UpdateNotesPanelVisibility();
    }

    private void ShowModeDetail(WorkMode mode)
    {
        HideAllPanels();
        ModeDetailPanel.Visibility = Visibility.Visible;
        FadeIn(ModeDetailPanel);

        TypewriterReveal(ModeDetailName, mode.Name);
        ModeDetailLaunchItems.ItemsSource = mode.LaunchItems;
        ModeDetailBlockItems.ItemsSource = mode.BlockItems;
        RefreshModeTimers(mode);
        ModeTimerBox.Text = "";
        UpdateModeTimerInput();

        _detailProject = null;
        UpdateNotesPanelVisibility();
    }

    private void GoalCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Goal goal } checkBox) return;

        goal.IsComplete = checkBox.IsChecked ?? false;
        _projectStore.Save(_projects);
        RefreshProjectProgress();
        PopAnimate(checkBox);
    }

    private static void PopAnimate(FrameworkElement element)
    {
        if (element.RenderTransform is not System.Windows.Media.ScaleTransform scale)
        {
            scale = new System.Windows.Media.ScaleTransform(1, 1);
            element.RenderTransform = scale;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var pop = new DoubleAnimation(1.3, TimeSpan.FromMilliseconds(160))
        {
            AutoReverse = true,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
        };
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pop);
    }

    private void RefreshProjectProgress()
    {
        if (_detailProject == null) return;

        var targetWidth = 320 * Math.Clamp(_detailProject.Completion, 0, 1);
        var anim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        ProjectProgressFill.BeginAnimation(FrameworkElement.WidthProperty, anim);

        ProjectProgressLabel.Text = $"{_detailProject.CompletionPercentText} complete — {_detailProject.NextGoalSummary}";
    }

    private void ShowProjectDetail(Project project)
    {
        HideAllPanels();
        ProjectDetailPanel.Visibility = Visibility.Visible;
        FadeIn(ProjectDetailPanel);
        _detailProject = project;

        TypewriterReveal(ProjectDetailName, project.Name);
        RefreshProjectProgress();

        var totalSeconds = _logService.GetForProject(project.Id).Sum(s => s.ActiveSeconds);
        ProjectTimeSpentText.Text = $"Time spent: {FormatSpan(totalSeconds)}";

        ProjectGoalsDisplay.ItemsSource = project.Goals.Count > 0 ? project.Goals : null;

        ProjectNotesDisplay.ItemsSource = project.Notes.Count > 0
            ? project.Notes.OrderByDescending(n => n.Timestamp)
                .Select(n => $"{n.Timestamp:MMM d, HH:mm} — {n.Text}").ToList()
            : new List<string> { "No notes yet." };

        LoadFreeformNotes(project);
        ApplyNoteKeybinds();
        UpdateNotesPanelVisibility();
    }

    // WPF's RichTextBox XAML serializers (both "Xaml" and "XamlPackage") silently drop
    // InlineUIContainer content, so checklist markers are round-tripped as private-use-area
    // sentinel characters in the text stream instead, and rebuilt into live markers after load.
    private const char UncheckedSentinel = '\uE000';
    private const char CheckedSentinel = '\uE001';

    private void LoadFreeformNotes(Project project)
    {
        NotesRichBox.Document = new FlowDocument();
        if (string.IsNullOrEmpty(project.FreeformNotesXaml))
        {
            // No rich content saved from Windows yet — if the Mac app wrote plain-text notes,
            // show those instead of an empty box, so notes written on Mac aren't invisible here.
            if (!string.IsNullOrEmpty(project.NotesPlainText))
            {
                foreach (var line in project.NotesPlainText.Split('\n'))
                    NotesRichBox.Document.Blocks.Add(new Paragraph(new Run(line.TrimEnd('\r'))));
            }
            return;
        }

        var range = new TextRange(NotesRichBox.Document.ContentStart, NotesRichBox.Document.ContentEnd);
        try
        {
            using var ms = new MemoryStream(Convert.FromBase64String(project.FreeformNotesXaml));
            range.Load(ms, DataFormats.XamlPackage);
            ReplaceSentinelsWithMarkers();
        }
        catch (FormatException)
        {
            try
            {
                // Legacy format: plain UTF8 XAML text (pre-checklist-marker saves).
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(project.FreeformNotesXaml));
                range.Load(ms, DataFormats.Xaml);
            }
            catch
            {
                // Corrupt or incompatible saved content; start fresh rather than crash.
            }
        }
        catch
        {
            // Corrupt or incompatible saved content; start fresh rather than crash.
        }

        RefreshNoteColors();
    }

    // Notes never carry a user-chosen text color (the toolbar has no color option), but
    // RichTextBox saves bake the effective foreground into the XAML — so notes saved under
    // a dark theme would load as near-white text under a light one. Strip every foreground
    // back to inherited (the RichTextBox's own theme-driven Foreground), then re-derive the
    // only colored text there is: muted completed checklist lines, plus the marker boxes.
    private void RefreshNoteColors()
    {
        var doc = NotesRichBox.Document;
        doc.ClearValue(FlowDocument.ForegroundProperty);
        ClearForeground(doc.Blocks);

        foreach (var block in doc.Blocks)
        {
            if (block is not Paragraph para) continue;
            foreach (var inline in para.Inlines.ToList())
            {
                if (inline is not InlineUIContainer { Child: Border { Uid: "checklist-marker" } marker } container)
                    continue;
                var isChecked = marker.Child is System.Windows.Shapes.Path;
                ApplyChecklistMarkerVisual(marker, isChecked);
                if (isChecked) StyleChecklistLineFrom(container.ElementEnd, true);
            }
        }
    }

    private static void ClearForeground(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            block.ClearValue(TextElement.ForegroundProperty);
            switch (block)
            {
                case Paragraph p: ClearForeground(p.Inlines); break;
                case Section s: ClearForeground(s.Blocks); break;
                case List l:
                    foreach (var item in l.ListItems) { item.ClearValue(TextElement.ForegroundProperty); ClearForeground(item.Blocks); }
                    break;
            }
        }
    }

    private static void ClearForeground(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            inline.ClearValue(TextElement.ForegroundProperty);
            if (inline is Span span) ClearForeground(span.Inlines);
        }
    }

    private void SaveFreeformNotes()
    {
        if (_detailProject == null) return;

        var swaps = new List<(Paragraph Para, InlineUIContainer Container, Run Sentinel)>();
        foreach (var block in NotesRichBox.Document.Blocks.ToList())
        {
            if (block is not Paragraph para) continue;
            foreach (var inline in para.Inlines.Cast<Inline>().ToList())
            {
                if (inline is not InlineUIContainer { Child: Border { Uid: "checklist-marker" } border } container)
                    continue;

                var isChecked = border.Child is System.Windows.Shapes.Path;
                var sentinel = new Run((isChecked ? CheckedSentinel : UncheckedSentinel).ToString());
                para.Inlines.InsertBefore(container, sentinel);
                para.Inlines.Remove(container);
                swaps.Add((para, container, sentinel));
            }
        }

        try
        {
            var range = new TextRange(NotesRichBox.Document.ContentStart, NotesRichBox.Document.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.XamlPackage);
            _detailProject.FreeformNotesXaml = Convert.ToBase64String(ms.ToArray());
        }
        finally
        {
            foreach (var (para, container, sentinel) in swaps)
            {
                para.Inlines.InsertBefore(sentinel, container);
                para.Inlines.Remove(sentinel);
            }
        }

        // Mac has no rich text editor, so it reads/writes notes as plain text. Mirror the
        // rich content into that shared field (checklist markers become literal "[ ]"/"[x]")
        // so notes written on Windows are still readable there, and vice versa.
        _detailProject.NotesPlainText = ExtractPlainTextWithChecklist();
        _projectStore.Save(_projects);
    }

    private string ExtractPlainTextWithChecklist()
    {
        var lines = new List<string>();
        foreach (var block in NotesRichBox.Document.Blocks)
        {
            if (block is not Paragraph para) continue;
            var sb = new System.Text.StringBuilder();
            foreach (var inline in para.Inlines)
            {
                if (inline is Run run)
                    sb.Append(run.Text);
                else if (inline is InlineUIContainer { Child: Border { Uid: "checklist-marker" } marker })
                    sb.Append(marker.Child is System.Windows.Shapes.Path ? "[x] " : "[ ] ");
            }
            lines.Add(sb.ToString());
        }
        return string.Join(Environment.NewLine, lines);
    }

    private void ReplaceSentinelsWithMarkers()
    {
        foreach (var block in NotesRichBox.Document.Blocks.ToList())
        {
            if (block is not Paragraph para) continue;
            foreach (var inline in para.Inlines.Cast<Inline>().ToList())
            {
                if (inline is not Run run) continue;
                var idx = run.Text.IndexOfAny(new[] { UncheckedSentinel, CheckedSentinel });
                if (idx < 0) continue;

                var isChecked = run.Text[idx] == CheckedSentinel;
                var before = run.Text[..idx];
                var after = run.Text[(idx + 1)..];

                var marker = new InlineUIContainer(CreateChecklistMarker(isChecked));
                para.Inlines.InsertBefore(run, marker);
                if (!string.IsNullOrEmpty(before))
                    para.Inlines.InsertBefore(marker, new Run(before));
                if (!string.IsNullOrEmpty(after))
                    para.Inlines.InsertAfter(marker, new Run(after));
                para.Inlines.Remove(run);
            }
        }
    }

    private void ShowHome()
    {
        HideAllPanels();
        HomePanel.Visibility = Visibility.Visible;
        FadeIn(HomePanel);
        SetNavHighlight();

        _detailProject = null;
        UpdateNotesPanelVisibility();

        var greeting = Greetings.Random();
        TypewriterReveal(HomeWelcomeText, DateTime.Today.ToString("dddd, MMMM d"), msPerChar: 8);
        TypewriterReveal(HomeGreetingText, greeting, msPerChar: 8);

        // Fresh from the log (another computer may have added time), then kept live by the tick.
        _todayLoggedDay = DateTime.Today;
        _todayLoggedSeconds = _logService.GetTodayTotalSeconds();
        UpdateTodayCard();
        BuildWeekBars(_logService.GetLastNDaysTotals(7));

        LoadDailyPlan();
        RefreshDailyProjectCombo();
        _ = RefreshBoardAsync();
        _ = RefreshGoogleGoalsAsync();

        // Opening the app lands here — be ready to type the day's first goal straight away.
        Dispatcher.BeginInvoke(() => DailyGoalInput.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void BuildWeekBars(Dictionary<DateTime, int> days)
    {
        const double barArea = 56;
        var max = Math.Max(1, days.Values.DefaultIfEmpty(0).Max());
        var columns = new List<UIElement>();

        foreach (var (day, seconds) in days.OrderBy(kv => kv.Key))
        {
            var isToday = day == DateTime.Today;
            var bar = new Border
            {
                Width = 14,
                Height = seconds == 0 ? 3 : Math.Max(4, barArea * seconds / max),
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Bottom,
                Opacity = seconds == 0 ? 0.25 : isToday ? 1 : 0.5
            };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

            var label = new TextBlock
            {
                Text = day.ToString("ddd")[..1],
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, isToday ? "TextBrush" : "MutedTextBrush");

            var column = new StackPanel { ToolTip = $"{day:dddd}: {FormatSpan(seconds)}" };
            column.Children.Add(new Grid { Height = barArea, Children = { bar } });
            column.Children.Add(label);
            columns.Add(column);
        }

        HomeWeekBars.ItemsSource = columns;
        HomeWeekText.Text = $"Last 7 days: {FormatSpan(days.Values.Sum())}";
    }

    private void HomeHeader_Click(object sender, MouseButtonEventArgs e)
    {
        ModesList.SelectedItem = null;
        ProjectsList.SelectedItem = null;
        ShowHome();
    }

    // ---------- Project board (formerly BijouBoard) ----------

    private ProjectBoardService.Board? _board;

    private DateTime _boardBuiltAt;

    // Home is visited often; BijouDocs/BijouMusic don't change that fast. Reuse a board built in
    // the last 30 seconds unless asked (the Refresh button) to re-read.
    private async Task RefreshBoardAsync(bool force = false)
    {
        if (!force && _board != null && DateTime.Now - _boardBuiltAt < TimeSpan.FromSeconds(30))
        {
            ShowBoard();
            return;
        }
        _boardBuiltAt = DateTime.Now;
        var projects = _projects.ToList();
        var sessions = _logService.GetAll();
        _board = await Task.Run(() => ProjectBoardService.Build(projects, sessions));
        ShowBoard();
    }

    // Separate from the refresh so a theme change can re-resolve the tone colors without re-reading files.
    private void ShowBoard()
    {
        if (_board == null) return;

        BoardCards.ItemsSource = null;
        BoardCards.ItemsSource = _board.Cards;
        BoardEmptyText.Visibility = _board.Cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        BoardSummary.Children.Clear();
        BoardSummary.Children.Add(BuildSummaryPill(_board.ReadyToPublish, "ready to publish", "SuccessBrush"));
        BoardSummary.Children.Add(BuildSummaryPill(_board.BehindPace, "behind pace", "DangerBrush"));
        BoardSummary.Children.Add(BuildSummaryPill(_board.NeedMusic, "need music", "HazardBrush"));
        BoardSummary.Children.Add(BuildSummaryPill(_board.Active, _board.Active == 1 ? "active project" : "active projects", "AccentBrush"));
    }

    private static Border BuildSummaryPill(int count, string label, string toneBrush)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, toneBrush);

        var number = new TextBlock { Text = count.ToString(), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        number.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");

        var pill = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 5, 14, 5),
            Margin = new Thickness(0, 0, 8, 8),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, number, text } }
        };
        pill.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        pill.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return pill;
    }

    private async void RefreshBoard_Click(object sender, RoutedEventArgs e) => await RefreshBoardAsync(force: true);

    private void BoardCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ProjectBoardService.Card card }) return;
        var project = card.Project;

        if (project == _activeProject)
        {
            ShowActiveSessionPanel();
            return;
        }

        SelectProjectAndShowDetail(project);
    }

    // ---------- Quick launch ----------

    private void RefreshQuickLaunchPanel()
    {
        QuickLaunchPanel.Children.Clear();

        foreach (var app in _quickLaunchApps)
            QuickLaunchPanel.Children.Add(BuildQuickLaunchTile(app));

        QuickLaunchPanel.Children.Add(BuildAddQuickLaunchTile());
    }

    private Button BuildQuickLaunchTile(QuickLaunchApp app)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new Image
        {
            Source = app.Icon,
            Width = 36,
            Height = 36,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var nameText = new TextBlock
        {
            Text = app.Name,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 84
        };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(nameText);

        var button = new Button
        {
            Content = stack,
            Tag = app,
            ToolTip = app.Name
        };
        button.SetResourceReference(StyleProperty, "QuickLaunchTileStyle");
        button.Click += QuickLaunchTile_Click;

        var removeItem = new MenuItem { Header = "Remove" };
        removeItem.Click += (_, _) => RemoveQuickLaunchApp(app);
        button.ContextMenu = new ContextMenu { Items = { removeItem } };

        return button;
    }

    private Button BuildAddQuickLaunchTile()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var plus = new TextBlock
        {
            Text = "+",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
        plus.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        stack.Children.Add(plus);
        var label = new TextBlock
        {
            Text = "Add App",
            FontSize = 11,
            TextAlignment = TextAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        stack.Children.Add(label);

        var button = new Button
        {
            Content = stack,
            ToolTip = "Add an app to Quick Launch"
        };
        button.SetResourceReference(StyleProperty, "QuickLaunchTileStyle");
        button.Click += AddQuickLaunchApp_Click;
        return button;
    }

    private void QuickLaunchTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QuickLaunchApp app } button) return;
        PopAnimate(button);

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = app.Path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't launch \"{app.Name}\":\n{ex.Message}", "Quick Launch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddQuickLaunchApp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;

        var app = new QuickLaunchApp
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
            Path = dlg.FileName
        };
        app.Icon = IconExtractor.GetIcon(app.Path);

        _quickLaunchApps.Add(app);
        _quickLaunchStore.Save(_quickLaunchApps);
        RefreshQuickLaunchPanel();
    }

    private void RemoveQuickLaunchApp(QuickLaunchApp app)
    {
        _quickLaunchApps.Remove(app);
        _quickLaunchStore.Save(_quickLaunchApps);
        RefreshQuickLaunchPanel();
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

        await BeginSession(dlg.SelectedMode, project, dlg.SelectedGoal, dlg.TargetMinutes, dlg.CountDown);
    }

    private async Task BeginSession(WorkMode? mode, Project? project, Goal? goal, int? targetMinutes, bool countDown = false,
        string? deckKeyId = null, PomodoroPlan? pomodoro = null)
    {
        // Starting while another session runs replaces it — log the running one first instead of
        // silently dropping its time.
        if (IsSessionActive)
            FinishSession(askForNote: false);

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

        if (mode?.DoNotDisturb == true) DoNotDisturb.TurnOn();

        _activeMode = mode;
        _activeProject = project;
        _activeGoal = goal;
        StartPomodoro(pomodoro);
        _targetMinutes = pomodoro == null ? targetMinutes : null;
        _countDownMode = countDown && _targetMinutes != null;
        _budgetAlertShown = false;
        _sessionStart = DateTime.Now;
        _activeSeconds = 0;
        _idleSeconds = 0;
        _isIdle = false;
        _paused = false;
        _deckKeyId = deckKeyId;
        SetIconButton(PauseToggleButton, "\uE769", "Pause");

        BlockNotifications.Children.Clear();
        _pendingBlockRows.Clear();
        CloseAllAlertWindows();
        _blockReminderTimer.Stop();

        TypewriterReveal(ActiveModeName, project?.Name ?? mode?.Name ?? "Session");
        UpdateSessionContextText();

        ActiveStatus.Text = _pomodoro != null ? PomodoroStatus : "Active";
        TimerDisplay.Text = _pomodoro != null ? PomodoroClock
            : _countDownMode && targetMinutes is int initialTarget ? TimeSpan.FromMinutes(initialTarget).ToString(@"hh\:mm\:ss")
            : "00:00:00";

        ShowActiveSessionPanel();

        if (mode != null)
            _blockWatcher.Start(mode);
        _tickTimer.Start();
        SoundFx.Play(SoundFx.Start);
        UpdateTaskbarProgress();
        BroadcastDeckState();
    }

    private void TickTimer_Tick(object? sender, EventArgs e)
    {
        var idleTime = IdleTimeService.GetIdleTime();

        if (_paused || _onBreak)
        {
            // Paused time (and a Pomodoro break) isn't worked time — it's logged alongside idle time.
            _idleSeconds++;
        }
        else if (idleTime >= IdleThreshold)
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

        if (_pomodoro != null)
        {
            if (!_paused) AdvancePomodoro();
            TimerDisplay.Text = PomodoroClock;
            ActiveStatus.Text = _paused ? "Paused" : _onBreak ? PomodoroStatus : _isIdle ? "Idle — timer paused" : PomodoroStatus;
        }
        else if (_countDownMode && _targetMinutes is int countDownTarget)
        {
            var remainingSeconds = Math.Max(0, countDownTarget * 60 - _activeSeconds);
            TimerDisplay.Text = TimeSpan.FromSeconds(remainingSeconds).ToString(@"hh\:mm\:ss");
        }
        else
        {
            TimerDisplay.Text = TimeSpan.FromSeconds(_activeSeconds).ToString(@"hh\:mm\:ss");
        }

        if (ActiveSessionBanner.Visibility == Visibility.Visible)
            ActiveSessionBannerText.Text = TimerDisplay.Text;

        _timerPopout?.UpdateDisplay(_activeProject?.Name ?? _activeMode?.Name ?? "Session", TimerDisplay.Text, ActiveStatus.Text);

        if (_targetMinutes is int target && !_budgetAlertShown && _activeSeconds >= target * 60)
        {
            _budgetAlertShown = true;
            ShowBudgetAlert(target);
        }

        // Checkpoint every 10s so a crash or force-kill loses at most a few seconds of
        // tracked time instead of the whole session — recovered on the next launch.
        if ((_activeSeconds + _idleSeconds) % 10 == 0)
            WriteSessionCheckpoint();

        if (HomePanel.Visibility == Visibility.Visible) UpdateTodayCard();
        UpdateTrayToolTip();
        UpdateTaskbarProgress();
        BroadcastDeckState();
    }

    private static string SessionCheckpointPath => Path.Combine(DataPaths.LocalDir, "active_session.json");

    private void WriteSessionCheckpoint()
    {
        var checkpoint = new SessionCheckpoint
        {
            StartTime = _sessionStart,
            ModeName = _activeMode?.Name ?? "",
            ProjectId = _activeProject?.Id,
            ProjectName = _activeProject?.Name,
            GoalId = _activeGoal?.Id,
            GoalName = _activeGoal?.Name,
            ActiveSeconds = _activeSeconds,
            IdleSeconds = _idleSeconds
        };

        try { AtomicFile.WriteAllText(SessionCheckpointPath, JsonSerializer.Serialize(checkpoint)); }
        catch { /* best effort — a missed checkpoint just means slightly more to lose on a crash */ }
    }

    private static void ClearSessionCheckpoint()
    {
        try { File.Delete(SessionCheckpointPath); }
        catch { /* nothing to clean up */ }
    }

    // Session ended by closing the app rather than clicking Finish Session — save the time
    // tracked so far without the finish-note prompt (a modal dialog during shutdown could
    // hang the close), then clear the checkpoint since it's now properly recorded.
    private void FinalizeSessionSilently()
    {
        _tickTimer.Stop();
        DoNotDisturb.Restore();

        if (_activeSeconds > 0)
        {
            _logService.InsertSession(new SessionRecord
            {
                ModeName = _activeMode?.Name ?? "",
                StartTime = _sessionStart,
                EndTime = DateTime.Now,
                ActiveSeconds = _activeSeconds,
                IdleSeconds = _idleSeconds,
                ProjectId = _activeProject?.Id,
                ProjectName = _activeProject?.Name,
                GoalId = _activeGoal?.Id,
                GoalName = _activeGoal?.Name,
                Note = null
            });
        }

        ClearSessionCheckpoint();
    }

    // Runs at startup. A leftover checkpoint means the app didn't reach the Closing handler
    // last time (crash, task-kill, power loss) — recover what was tracked up to the last
    // checkpoint instead of silently losing it.
    private void RecoverInterruptedSessionIfAny()
    {
        if (!File.Exists(SessionCheckpointPath)) return;

        try
        {
            var checkpoint = JsonSerializer.Deserialize<SessionCheckpoint>(File.ReadAllText(SessionCheckpointPath));
            if (checkpoint != null && checkpoint.ActiveSeconds > 0)
            {
                _logService.InsertSession(new SessionRecord
                {
                    ModeName = checkpoint.ModeName,
                    StartTime = checkpoint.StartTime,
                    EndTime = checkpoint.StartTime.AddSeconds(checkpoint.ActiveSeconds + checkpoint.IdleSeconds),
                    ActiveSeconds = checkpoint.ActiveSeconds,
                    IdleSeconds = checkpoint.IdleSeconds,
                    ProjectId = checkpoint.ProjectId,
                    ProjectName = checkpoint.ProjectName,
                    GoalId = checkpoint.GoalId,
                    GoalName = checkpoint.GoalName,
                    Note = "Recovered after BijouHub closed unexpectedly"
                });

                var minutes = Math.Max(1, checkpoint.ActiveSeconds / 60);
                var suffix = checkpoint.ProjectName != null ? $" on \"{checkpoint.ProjectName}\"." : ".";
                MessageBox.Show(
                    $"BijouHub didn't close normally last time — recovered {minutes} minute{(minutes == 1 ? "" : "s")} of tracked time{suffix}",
                    "Session Recovered", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch
        {
            // Corrupt checkpoint — nothing usable to recover.
        }
        finally
        {
            ClearSessionCheckpoint();
        }
    }

    // Icon-only buttons swap glyph and tooltip (and their accessible name) to show state.
    private static void SetIconButton(Button button, string glyph, string label)
    {
        if (button.Content is TextBlock icon) icon.Text = glyph;
        button.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
    }

    private void UpdateSessionContextText()
    {
        var contextParts = new List<string>();
        if (_activeProject != null && _activeMode != null) contextParts.Add($"via {_activeMode.Name}");
        if (_activeGoal != null) contextParts.Add($"working on: {_activeGoal.Name}");
        if (_targetMinutes is int tm) contextParts.Add(_countDownMode ? $"counting down from {tm} min" : $"budget: {tm} min");
        if (_pomodoro != null)
            contextParts.Add($"Pomodoro {_pomodoro.FocusMinutes} / {_pomodoro.BreakMinutes} min" + (_pomodoro.Rounds is int r ? $" × {r}" : ""));
        if (_activeMode?.DoNotDisturb == true) contextParts.Add("Do Not Disturb");
        ActiveContextText.Text = string.Join("  •  ", contextParts);
        ActiveContextText.Visibility = contextParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Adds time to the running session: more countdown (or budget), or — for a session that was
    // counting up — a countdown of that length from now. Clears a "time's up" alert.
    private bool ExtendSession(int minutes)
    {
        if (!IsSessionActive || minutes <= 0) return false;

        if (_pomodoro != null)
        {
            ExtendPomodoroPhase(minutes);
            BroadcastDeckState();
            return true;
        }

        if (_targetMinutes is int target)
        {
            // Past the end already: count the extension from now, not from the old target.
            var elapsedMinutes = (int)Math.Ceiling(_activeSeconds / 60.0);
            _targetMinutes = Math.Max(target, elapsedMinutes) + minutes;
        }
        else
        {
            _targetMinutes = (int)Math.Ceiling(_activeSeconds / 60.0) + minutes;
            _countDownMode = true;
        }

        _budgetAlertShown = false;
        _budgetAlertWindow?.Close();
        _budgetAlertWindow = null;
        UpdateSessionContextText();
        BroadcastDeckState();
        return true;
    }

    private void ShowBudgetAlert(int targetMinutes)
    {
        SoundFx.Play(SoundFx.End);

        _budgetAlertWindow?.Close();
        var alert = new BudgetAlertWindow(_activeProject?.Name ?? _activeMode?.Name ?? "session", targetMinutes);
        var workArea = SystemParameters.WorkArea;
        alert.Left = workArea.Right - alert.Width - 16;
        alert.Top = workArea.Bottom - alert.Height - 16;

        alert.Extended += () => ExtendSession(15);
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

    private void OnHardBlockedProcessClosed(string processName)
    {
        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(new TextBlock
        {
            Text = $"{processName} was closed automatically — hard-blocked in this mode",
            VerticalAlignment = VerticalAlignment.Center
        });
        BlockNotifications.Children.Add(row);
        System.Media.SystemSounds.Asterisk.Play();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            BlockNotifications.Children.Remove(row);
        };
        timer.Start();
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

    private void FinishSession_Click(object sender, RoutedEventArgs e) => FinishSession(askForNote: true);

    // askForNote is false when the session is ended from the Stream Deck — a modal prompt
    // would pop up mid-work with nobody at the window expecting it.
    private void FinishSession(bool askForNote)
    {
        if (_activeMode == null && _activeProject == null) return;

        _tickTimer.Stop();
        _blockWatcher.Stop();
        _blockReminderTimer.Stop();
        _pendingBlockRows.Clear();
        CloseAllAlertWindows();
        _timerPopout?.Close();
        _timerPopout = null;

        string? note = null;
        var finishedProject = _activeProject;
        if (finishedProject != null && askForNote)
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
        InvalidateTodayLogged();
        ClearSessionCheckpoint();

        var finishedMode = _activeMode;
        _activeMode = null;
        _activeProject = null;
        _activeGoal = null;
        _targetMinutes = null;
        _countDownMode = false;
        _paused = false;
        _deckKeyId = null;
        StartPomodoro(null);
        DoNotDisturb.Restore();
        UpdateTaskbarProgress();
        BroadcastDeckState();

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

    // ---------- Today's goals ----------

    private readonly DailyPlanStore _dailyStore = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<DailyGoal> _dailyGoals = new();
    private DailyPlan? _today;
    private bool _loadingDailyPlan;
    private bool _reorderingGoals;
    private System.Windows.Threading.DispatcherTimer? _notesSaveTimer;
    private List<DailyGoal> _carryOverCandidates = new();

    // Set up once: the reorder behavior and change tracking hold on to this one collection.
    private void InitDailyGoals()
    {
        DailyGoalsList.ItemsSource = _dailyGoals;
        ListReorderBehavior.Enable(DailyGoalsList, _dailyGoals);

        // The list's own (disabled) scroller would swallow the wheel; let the home page scroll instead.
        DailyGoalsList.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            HomePanel.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent });
        };
        _dailyGoals.CollectionChanged += (_, e) =>
        {
            if (_loadingDailyPlan || _applyingRemote) return;
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move && !_reorderingGoals)
                PinStarredGoals();
            SaveDailyPlan();
        };

        // The dropdown's popup takes focus outside the bar; keep the picker shown, then hand typing back.
        DailyGoalProjectCombo.DropDownOpened += (_, _) => UpdateAddGoalBar();
        DailyGoalProjectCombo.DropDownClosed += (_, _) =>
        {
            DailyGoalInput.Focus();
            UpdateAddGoalBar();
        };

        _notesSaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _notesSaveTimer.Tick += (_, _) =>
        {
            _notesSaveTimer.Stop();
            SaveDailyPlan();
        };
    }

    // Reloads only when the day changed (or on first show), so edits in progress aren't clobbered.
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

    private void UpdateDailyProgress()
    {
        var visible = _dailyGoals.Where(InScope).ToList();
        var total = visible.Count;
        var done = visible.Count(g => g.Done);
        BuildScopeTabs(ScopeGroups());
        RefreshDayTabs();
        RefreshTasksPage();
        DailyProgressText.Text = total == 0 ? "" : _dayView switch
        {
            DayView.Tomorrow => $"{total} planned",
            DayView.Upcoming => $"{total} upcoming",
            _ => done == total ? $"All {total} done" : $"{done} of {total} done"
        };
        DailyGoalsList.Visibility = total == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DailyGoal_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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
        _reorderingGoals = true;
        try
        {
            for (var i = 0; i < desired.Count; i++)
            {
                var current = _dailyGoals.IndexOf(desired[i]);
                if (current != i) _dailyGoals.Move(current, i);
            }
        }
        finally
        {
            _reorderingGoals = false;
        }
    }

    private void AddDailyGoal(DailyGoal goal)
    {
        goal.ChipText = ChipFor(goal);
        goal.PropertyChanged += DailyGoal_PropertyChanged;
        _dailyGoals.Add(goal);
        if (goal.Starred) PinStarredGoals();
        if (GoogleMode) QueueGoogleCreate(goal);
    }

    private void RemoveDailyGoal(DailyGoal goal)
    {
        goal.PropertyChanged -= DailyGoal_PropertyChanged;
        _dailyGoals.Remove(goal);
        PushGoalDeleted(goal);
    }

    private void DailyGoalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DailyGoalInput.Clear();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var text = DailyGoalInput.Text.Trim();
        if (text.Length == 0) return;

        LoadDailyPlan(); // past midnight, the new goal belongs to the new day
        AddDailyGoal(NewGoal(text));
        DailyGoalInput.Clear();
        ClearPendingDue();
    }

    // Clicking anywhere on the bar (the "+", the padding) puts the cursor in it.
    private void AddGoalBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInside<ComboBox>(source)) return;
        DailyGoalInput.Focus();
        e.Handled = true;
    }

    private static bool IsInside<T>(DependencyObject element) where T : DependencyObject
    {
        for (var current = element; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is T) return true;
        return false;
    }

    private void AddGoalBar_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateAddGoalBar();

    private void DailyGoalProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((DailyGoalProjectCombo.SelectedItem as ComboBoxItem)?.Tag == NewListTag)
        {
            CreateGoogleListFromPicker();
            return;
        }
        UpdateAddGoalBar();
    }

    // The project picker stays out of the way until the bar is in use, or a project is picked.
    private void UpdateAddGoalBar()
    {
        var active = AddGoalBar.IsKeyboardFocusWithin || DailyGoalProjectCombo.IsDropDownOpen || (DuePopup.IsOpen && _dueTarget == null);
        UpdateAddGoalDue(active);
        var projectPicked = PickedTarget().Name != null;
        DailyGoalProjectCombo.Visibility = active || projectPicked ? Visibility.Visible : Visibility.Collapsed;
        AddGoalGlyph.Text = active ? "○" : "+";
        AddGoalGlyph.FontSize = active ? 18 : 22;
    }

    private void DailyGoalInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        DailyGoalPlaceholder.Visibility = DailyGoalInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        // A pasted list arrives in one go — each line becomes its own goal.
        if (DailyGoalInput.Text.IndexOfAny(new[] { '\n', '\r' }) < 0) return;
        var lines = DailyGoalInput.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DailyGoalInput.Clear();
        LoadDailyPlan();
        foreach (var line in lines)
            AddDailyGoal(NewGoal(line.TrimStart('-', '*', '•', ' ')));
        ClearPendingDue(); // a pasted list all gets the date that was picked
    }

    private static readonly object NewListTag = new();

    // The list new goals go to, from the current tab's lists (with no Google connection: a
    // project or none). Keeps its choice across refreshes so a run of goals for one list only
    // needs picking once.
    private void RefreshDailyProjectCombo(string? selectKey = null)
    {
        var keep = selectKey ?? PickedTarget().Key;

        DailyGoalProjectCombo.Items.Clear();
        foreach (var target in TargetsFor(_goalScope))
            DailyGoalProjectCombo.Items.Add(new ComboBoxItem { Content = target.Label, Tag = target });
        if (GoogleMode)
            DailyGoalProjectCombo.Items.Add(new ComboBoxItem { Content = "+ New list…", Tag = NewListTag });

        var items = DailyGoalProjectCombo.Items.OfType<ComboBoxItem>().ToList();
        DailyGoalProjectCombo.SelectedItem = items.FirstOrDefault(i => (i.Tag as ListTarget)?.Key == keep) ?? items.FirstOrDefault();
    }

    private ListTarget PickedTarget() =>
        (DailyGoalProjectCombo.SelectedItem as ComboBoxItem)?.Tag as ListTarget
        ?? TargetsFor(_goalScope).FirstOrDefault()
        ?? new ListTarget(EditingGroup, null, null, "No project");

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

    private static DailyGoal? GoalOf(object sender) => (sender as FrameworkElement)?.DataContext as DailyGoal;

    private void DailyGoalStar_Click(object sender, RoutedEventArgs e)
    {
        if (GoalOf(sender) is DailyGoal goal) goal.Starred = !goal.Starred;
    }

    private void DailyGoalDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GoalOf(sender) is DailyGoal goal) RemoveDailyGoal(goal);
    }

    private void DailyGoalText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || GoalOf(sender) is not DailyGoal goal) return;
        goal.IsEditing = true;
        e.Handled = true;
    }

    private void DailyGoalEditor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || !box.IsVisible) return;
        box.Text = GoalOf(box)?.Text ?? "";
        box.Focus();
        box.SelectAll();
    }

    private void DailyGoalEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || GoalOf(box) is not DailyGoal goal) return;
        if (e.Key == Key.Enter)
        {
            CommitGoalEdit(box, goal);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            goal.IsEditing = false;
            e.Handled = true;
        }
    }

    private void DailyGoalEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box && GoalOf(box) is DailyGoal { IsEditing: true } goal)
            CommitGoalEdit(box, goal);
    }

    private static void CommitGoalEdit(TextBox box, DailyGoal goal)
    {
        var text = box.Text.Trim();
        if (text.Length > 0) goal.Text = text;
        goal.IsEditing = false;
    }

    private void DailyGoalChip_Click(object sender, MouseButtonEventArgs e)
    {
        // The chip opens the same menu as right-clicking the row.
        if (sender is not FrameworkElement chip) return;
        var row = (FrameworkElement)VisualTreeHelperParentGrid(chip);
        if (row.ContextMenu == null) return;
        BuildGoalMenu(row.ContextMenu, (DailyGoal)row.DataContext);
        row.ContextMenu.PlacementTarget = chip;
        row.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private static DependencyObject VisualTreeHelperParentGrid(DependencyObject element)
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(element);
        while (current is not null && !(current is Grid { ContextMenu: not null }))
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        return current ?? element;
    }

    private void DailyGoalRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu, DataContext: DailyGoal goal })
            BuildGoalMenu(menu, goal);
    }

    private void BuildGoalMenu(ContextMenu menu, DailyGoal goal)
    {
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
        due.Click += (_, _) => OpenDuePicker(menu.PlacementTarget ?? DailyGoalsList, goal);
        menu.Items.Add(due);

        var link = new MenuItem { Header = GoogleMode ? "Move to list" : "Link to project" };
        var menuScope = !GoogleMode ? EditingGroup : _goalScope == AllScope ? AllScope : GoogleGoalsSync.GroupOf(goal);
        foreach (var target in TargetsFor(menuScope))
        {
            var item = new MenuItem { Header = target.Label, IsCheckable = true, IsChecked = IsTargetOf(target, goal) };
            item.Click += (_, _) => RelinkGoal(goal, target);
            link.Items.Add(item);
        }
        menu.Items.Add(link);

        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => RemoveDailyGoal(goal);
        menu.Items.Add(delete);
    }

    private void DailyNotesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingDailyPlan || _notesSaveTimer == null) return;
        _notesSaveTimer.Stop();
        _notesSaveTimer.Start();
    }

    private void DailyNotesBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_notesSaveTimer?.IsEnabled != true) return;
        _notesSaveTimer.Stop();
        SaveDailyPlan();
    }

    // Offers the most recent earlier day's unfinished goals, once per day.
    private void RefreshCarryOver()
    {
        _carryOverCandidates.Clear();
        if (!GoogleMode && _dayView == DayView.Today && _today is { CarryOverHandled: false } && _dailyStore.LatestUnfinishedBefore(DateTime.Today) is var (date, goals))
        {
            var carried = _dailyGoals.Select(g => g.CarriedFromId).ToHashSet();
            _carryOverCandidates = goals.Where(g => !carried.Contains(g.Id)).ToList();
            if (_carryOverCandidates.Count > 0)
            {
                var day = DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var when = day == DateTime.Today.AddDays(-1) ? "yesterday" : day.ToString("dddd");
                var count = _carryOverCandidates.Count;
                CarryOverButton.Content = $"↪  Carry over {count} unfinished goal{(count == 1 ? "" : "s")} from {when}";
            }
        }
        CarryOverRow.Visibility = _carryOverCandidates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CarryOver_Click(object sender, RoutedEventArgs e)
    {
        foreach (var old in _carryOverCandidates)
        {
            AddDailyGoal(new DailyGoal
            {
                Text = old.Text,
                Starred = old.Starred,
                ProjectId = old.ProjectId,
                ProjectName = old.ProjectName,
                CarriedFromId = old.Id
            });
        }
        DismissCarryOver_Click(sender, e);
    }

    private void DismissCarryOver_Click(object sender, RoutedEventArgs e)
    {
        if (_today == null) return;
        _today.CarryOverHandled = true;
        SaveDailyPlan();
        _carryOverCandidates.Clear();
        CarryOverRow.Visibility = Visibility.Collapsed;
    }

    // ---------- Mode timers ----------

    private void RefreshModeTimers(WorkMode mode)
    {
        ModeTimerTiles.Children.Clear();
        foreach (var minutes in mode.TimerMinutes.Order())
            ModeTimerTiles.Children.Add(BuildModeTimerTile(mode, minutes));
        foreach (var plan in mode.PomodoroTimers.Select(DurationText.TryParsePomodoro).OfType<PomodoroPlan>())
            ModeTimerTiles.Children.Add(BuildPomodoroTile(mode, plan));
    }

    private Button BuildModeTimerTile(WorkMode mode, int minutes)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var play = new TextBlock { Text = "", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) };
        play.SetResourceReference(StyleProperty, "IconGlyph");
        play.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        stack.Children.Add(play);

        // Same face as the timer screen, so the tile reads as the countdown it starts.
        var length = new TextBlock { Text = DurationText.Format(minutes), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center };
        length.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        length.SetResourceReference(TextBlock.FontFamilyProperty, "TimerFont");
        length.SetResourceReference(TextBlock.FontWeightProperty, "TimerFontWeight");
        stack.Children.Add(length);

        var button = new Button
        {
            Content = stack,
            Height = 72,
            ToolTip = $"Launch {mode.Name} and count down from {DurationText.Format(minutes)}"
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{DurationText.Format(minutes)} timer");
        button.SetResourceReference(StyleProperty, "QuickLaunchTileStyle");
        button.Click += async (_, _) => await StartModeTimer(mode, minutes);

        var remove = new MenuItem { Header = "Remove timer" };
        remove.Click += (_, _) =>
        {
            mode.TimerMinutes.Remove(minutes);
            _modeStore.Save(_modes);
            RefreshModeTimers(mode);
            UpdateModeTimerInput();
        };
        button.ContextMenu = new ContextMenu { Items = { remove } };
        return button;
    }

    private Task StartModeTimer(WorkMode mode, int minutes) =>
        BeginSession(mode, null, null, minutes, countDown: true);

    private Task StartModePomodoro(WorkMode mode, PomodoroPlan plan) =>
        BeginSession(mode, null, null, plan.FocusMinutes, pomodoro: plan);

    // A saved "25/5": focus and break lengths on one tile.
    private Button BuildPomodoroTile(WorkMode mode, PomodoroPlan plan)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var label = new TextBlock { Text = plan.Rounds is int r ? $"POMODORO ×{r}" : "POMODORO", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        stack.Children.Add(label);

        var length = new TextBlock { Text = $"{plan.FocusMinutes}/{plan.BreakMinutes}", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center };
        length.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        length.SetResourceReference(TextBlock.FontFamilyProperty, "TimerFont");
        length.SetResourceReference(TextBlock.FontWeightProperty, "TimerFontWeight");
        stack.Children.Add(length);

        var button = new Button
        {
            Content = stack,
            Height = 72,
            ToolTip = $"Launch {mode.Name}: {plan.FocusMinutes} min focus, {plan.BreakMinutes} min break, " + (plan.Rounds is int n ? $"{n} rounds" : "repeating")
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"Pomodoro {plan.FocusMinutes} {plan.BreakMinutes}" + (plan.Rounds is int count ? $" x{count}" : ""));
        button.SetResourceReference(StyleProperty, "QuickLaunchTileStyle");
        button.Click += async (_, _) => await StartModePomodoro(mode, plan);

        var remove = new MenuItem { Header = "Remove timer" };
        remove.Click += (_, _) =>
        {
            mode.PomodoroTimers.Remove(plan.ToString());
            _modeStore.Save(_modes);
            RefreshModeTimers(mode);
            UpdateModeTimerInput();
        };
        button.ContextMenu = new ContextMenu { Items = { remove } };
        return button;
    }

    // The timer box takes a length ("45") or a Pomodoro ("25/5").
    private async Task StartFromTimerBox()
    {
        if (ModesList.SelectedItem is not WorkMode mode) return;
        if (DurationText.TryParsePomodoro(ModeTimerBox.Text) is PomodoroPlan plan) await StartModePomodoro(mode, plan);
        else if (DurationText.TryParseMinutes(ModeTimerBox.Text) is int minutes) await StartModeTimer(mode, minutes);
    }

    private void ModeTimerBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateModeTimerInput();

    private async void ModeTimerBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await StartFromTimerBox();
    }

    private async void ModeTimerStart_Click(object sender, RoutedEventArgs e) => await StartFromTimerBox();

    private void ModeTimerSave_Click(object sender, RoutedEventArgs e)
    {
        if (ModesList.SelectedItem is not WorkMode mode) return;
        if (DurationText.TryParsePomodoro(ModeTimerBox.Text) is PomodoroPlan plan)
        {
            if (mode.PomodoroTimers.Contains(plan.ToString())) return;
            mode.PomodoroTimers.Add(plan.ToString());
            _modeStore.Save(_modes);
            RefreshModeTimers(mode);
            ModeTimerBox.Text = "";
            return;
        }
        if (DurationText.TryParseMinutes(ModeTimerBox.Text) is not int minutes) return;
        if (mode.TimerMinutes.Contains(minutes)) return;

        mode.TimerMinutes.Add(minutes);
        _modeStore.Save(_modes);
        RefreshModeTimers(mode);
        ModeTimerBox.Text = "";
    }

    private void UpdateModeTimerInput()
    {
        var text = ModeTimerBox.Text;
        var minutes = DurationText.TryParseMinutes(text);
        var pomodoro = DurationText.TryParsePomodoro(text);
        var selected = ModesList.SelectedItem as WorkMode;
        var alreadySaved = selected != null &&
            (pomodoro != null ? selected.PomodoroTimers.Contains(pomodoro.ToString()) : minutes is int m && selected.TimerMinutes.Contains(m));
        var valid = minutes != null || pomodoro != null;

        ModeTimerStartButton.IsEnabled = valid;
        ModeTimerSaveButton.IsEnabled = valid && !alreadySaved;
        ModeTimerSaveButton.ToolTip = alreadySaved ? "Already saved on this mode" : "Save this length as a timer on the mode";

        string hint;
        string brush = "MutedTextBrush";
        if (string.IsNullOrWhiteSpace(text))
            hint = "";
        else if (pomodoro != null)
            hint = $"{pomodoro.FocusMinutes} min focus, {pomodoro.BreakMinutes} min break, " + (pomodoro.Rounds is int rounds ? $"{rounds} rounds." : "on repeat. Add x4 for 4 rounds.");
        else if (minutes is int length)
            hint = $"Counts down from {DurationText.Format(length)}.";
        else
        {
            hint = "Can't read that — try 45, 1:30, 2h, or 25/5 (25/5x4) for a Pomodoro.";
            brush = "DangerBrush";
        }
        ModeTimerHint.Text = hint;
        ModeTimerHint.Visibility = hint.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ModeTimerHint.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }

    // ---------- Pause ----------

    private void PauseToggle_Click(object sender, RoutedEventArgs e) => TogglePause();

    private void TogglePause()
    {
        if (!IsSessionActive) return;

        _paused = !_paused;
        _isIdle = false;
        ActiveStatus.Text = _paused ? "Paused" : _pomodoro != null ? PomodoroStatus : "Active";
        SetIconButton(PauseToggleButton, _paused ? "\uE768" : "\uE769", _paused ? "Resume" : "Pause");
        _timerPopout?.UpdateDisplay(_activeProject?.Name ?? _activeMode?.Name ?? "Session", TimerDisplay.Text, ActiveStatus.Text);
        BroadcastDeckState();
    }

    // ---------- Stream Deck ----------

    private JsonObject DeckState() => new()
    {
        ["type"] = "state",
        ["active"] = IsSessionActive,
        ["keyId"] = _deckKeyId,
        ["title"] = _activeProject?.Name ?? _activeMode?.Name,
        ["targetSeconds"] = _countDownMode && _targetMinutes is int target ? target * 60 : null,
        ["activeSeconds"] = _activeSeconds,
        ["paused"] = _paused,
        ["idle"] = _isIdle && !_paused,
        ["todaySeconds"] = TodayLoggedSeconds() + (IsSessionActive ? _activeSeconds : 0),
        ["poppedOut"] = _timerPopout != null,
        ["pomodoro"] = DeckPomodoro(),
        ["dailyTargetSeconds"] = _dailyTargetMinutes * 60
    };

    private void BroadcastDeckState() => _deckBridge.Broadcast(DeckState());

    private DateTime _todayLoggedDay;
    private int _todayLoggedSeconds;

    // Time already logged today, read once per day and refreshed whenever a session is saved.
    private int TodayLoggedSeconds()
    {
        if (_todayLoggedDay != DateTime.Today) InvalidateTodayLogged();
        return _todayLoggedSeconds;
    }

    private void InvalidateTodayLogged()
    {
        _boardBuiltAt = DateTime.MinValue; // time per project changed too
        _todayLoggedDay = DateTime.Today;
        _todayLoggedSeconds = _logService.GetTodayTotalSeconds();
    }

    // Open goals for the deck's Next Goal key: every tab, starred first, in the user's order.
    private JsonObject DeckGoals()
    {
        var today = _dailyGoals.Where(g => !g.IsPlannedAfter(TodayKey)).ToList();
        var open = today.Where(g => !g.Done).OrderBy(g => g.Starred ? 0 : 1).Take(30).ToList();
        return new JsonObject
        {
            ["type"] = "goals",
            ["open"] = today.Count(g => !g.Done),
            ["done"] = today.Count(g => g.Done),
            ["items"] = new JsonArray(open.Select(g => (JsonNode)new JsonObject
            {
                ["id"] = g.Id,
                ["text"] = g.Text,
                ["starred"] = g.Starred,
                ["label"] = g.ChipText
            }).ToArray())
        };
    }

    private void BroadcastDeckGoals() => _deckBridge.Broadcast(DeckGoals());

    // Brings BijouHub forward when a key asks (e.g. tapping Current Session with nothing running).
    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        // Windows won't always let a background app take focus; a topmost flick still raises it.
        Topmost = true;
        Topmost = false;
        Focus();
    }

    // Runs on the UI thread (the bridge marshals every command here). The reply goes back to
    // the plugin that asked; state changes also reach every connected plugin via broadcast.
    private JsonObject? HandleDeckCommand(JsonObject request)
    {
        switch ((string?)request["type"])
        {
            case "catalog":
                return new JsonObject
                {
                    ["modes"] = new JsonArray(_modes.Select(m => (JsonNode)new JsonObject { ["id"] = m.Id, ["name"] = m.Name }).ToArray()),
                    ["projects"] = new JsonArray(_projects.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name }).ToArray())
                };

            case "state":
                return DeckState();

            case "start":
                return StartFromDeck(request);

            case "pause":
                TogglePause();
                return DeckState();

            case "finish":
                FinishSession(askForNote: false);
                return DeckState();

            case "extend":
                var extendBy = request["minutes"] is JsonValue ev && ev.TryGetValue<int>(out var em) ? em : 15;
                return ExtendSession(extendBy) ? DeckState() : new JsonObject { ["error"] = "No session is running" };

            case "goals":
                return DeckGoals();

            case "completeGoal":
                var goalId = (string?)request["goalId"]; // "id" is the request id the bridge replies with
                var done = _dailyGoals.FirstOrDefault(g => g.Id == goalId);
                if (done == null) return new JsonObject { ["error"] = "That goal is gone" };
                done.Done = true; // saves, syncs to Google and re-broadcasts the list
                return DeckGoals();

            case "focus":
                BringToFront();
                return new JsonObject { ["ok"] = true };

            case "capture":
                // After replying, so the plugin isn't left waiting on a window.
                Dispatcher.BeginInvoke(ShowQuickCapture);
                return new JsonObject { ["ok"] = true };

            case "target":
                Dispatcher.BeginInvoke(() =>
                {
                    BringToFront();
                    PromptDailyTarget();
                });
                return new JsonObject { ["ok"] = true };

            case "popout":
                if (!IsSessionActive) return new JsonObject { ["error"] = "No session is running" };
                // "show" picks a side; without it the key toggles.
                SetTimerPopout(request["show"] is JsonValue sv && sv.TryGetValue<bool>(out var show) ? show : _timerPopout == null);
                return DeckState();

            default:
                return new JsonObject { ["error"] = "Unknown command" };
        }
    }

    private JsonObject StartFromDeck(JsonObject request)
    {
        var keyId = (string?)request["keyId"];
        var modeId = (string?)request["modeId"];
        var modeName = (string?)request["modeName"];
        var projectId = (string?)request["projectId"];
        var minutes = request["minutes"] is JsonValue m && m.TryGetValue<int>(out var parsed) && parsed > 0 ? parsed : (int?)null;
        // A Pomodoro key sends its break too: focus for `minutes`, break for `breakMinutes`, repeat.
        var rounds = request["rounds"] is JsonValue rv && rv.TryGetValue<int>(out var r) && r > 0 ? r : (int?)null;
        var pomodoro = minutes is int focus && request["breakMinutes"] is JsonValue b && b.TryGetValue<int>(out var rest) && rest > 0
            ? new PomodoroPlan(focus, rest, rounds)
            : null;

        // Match by id, then by name, so a key still works after its mode is recreated.
        var mode = _modes.FirstOrDefault(x => x.Id == modeId)
                   ?? _modes.FirstOrDefault(x => string.Equals(x.Name, modeName, StringComparison.OrdinalIgnoreCase));
        var project = string.IsNullOrEmpty(projectId) ? null : _projects.FirstOrDefault(x => x.Id == projectId);
        if (mode == null && project == null)
            return new JsonObject { ["error"] = "Mode not found — pick one in the key's settings" };

        // A second press while modes are still launching would otherwise start twice.
        if (_sessionStarting) return DeckState();

        // A session already running (from the app or another key) is logged and replaced by BeginSession.
        _ = StartDeckSessionAsync(mode, project, minutes, keyId, pomodoro);
        return new JsonObject { ["ok"] = true };
    }

    private async Task StartDeckSessionAsync(WorkMode? mode, Project? project, int? minutes, string? keyId, PomodoroPlan? pomodoro)
    {
        _sessionStarting = true;
        try
        {
            if (project != null) ProjectsList.SelectedItem = project;
            await BeginSession(mode, project, null, minutes, countDown: minutes != null, deckKeyId: keyId, pomodoro: pomodoro);
        }
        finally
        {
            _sessionStarting = false;
        }
    }

    private void OpenStreamDeck_Click(object sender, RoutedEventArgs e)
    {
        var win = new StreamDeckWindow(() => _deckBridge.ClientCount) { Owner = this };
        win.ShowDialog();
    }

    // ---------- Session log ----------

    private void SessionLog_Click(object sender, RoutedEventArgs e)
    {
        var win = new SessionLogWindow(_logService, _projects) { Owner = this };
        win.ShowDialog();

        // Time moved onto a project changes its "time spent" and the home totals.
        if (!win.AssignmentsChanged) return;
        _boardBuiltAt = DateTime.MinValue;
        if (ProjectDetailPanel.Visibility == Visibility.Visible && _detailProject != null)
            ShowProjectDetail(_detailProject);
        else if (HomePanel.Visibility == Visibility.Visible)
            ShowHome();
    }

    // ---------- Sync folder ----------

    private void UpdateStartupButtonLabel()
    {
        var on = StartupService.IsEnabled;
        StartupToggleButton.ToolTip = on ? "Starts with Windows — click to turn off" : "Start with Windows";
        SetToolActive(StartupToggleButton, on);
    }

    // A sidebar tool that's switched on shows in the accent color.
    private static void SetToolActive(Button tool, bool active)
    {
        if (active) tool.SetResourceReference(ForegroundProperty, "AccentBrush");
        else tool.ClearValue(ForegroundProperty);
    }

    private void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        StartupService.SetEnabled(!StartupService.IsEnabled);
        UpdateStartupButtonLabel();
    }

    private void UpdateSyncFolderButtonLabel()
    {
        var settings = _settingsStore.Load();
        var synced = !string.IsNullOrEmpty(settings.DataFolderPath);
        SyncFolderButton.ToolTip = synced
            ? $"Syncing projects and history via {settings.DataFolderPath} — click to change"
            : "Sync folder: keep projects and history in a folder you sync across devices";
        SetToolActive(SyncFolderButton, synced);
    }

    private void SyncFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to sync Projects & session history through" };
        if (dlg.ShowDialog() != true) return;

        var chosen = dlg.FolderName;
        var hasExistingSyncedData = File.Exists(System.IO.Path.Combine(chosen, "projects.json"))
            || File.Exists(System.IO.Path.Combine(chosen, "sessions.json"));

        if (!hasExistingSyncedData)
        {
            // First time pointing here: bring existing data along so nothing's lost.
            foreach (var fileName in new[] { "projects.json", "sessions.json" })
            {
                var source = System.IO.Path.Combine(DataPaths.SyncDir, fileName);
                var dest = System.IO.Path.Combine(chosen, fileName);
                if (File.Exists(source) && !File.Exists(dest))
                    File.Copy(source, dest);
            }
        }

        var settings = _settingsStore.Load();
        settings.DataFolderPath = chosen;
        _settingsStore.Save(settings);
        UpdateSyncFolderButtonLabel();

        var restart = MessageBox.Show(
            hasExistingSyncedData
                ? "Found existing BijouHub data in this folder — it'll be used from now on.\n\nRestart now to apply?"
                : "Your projects and session history have been copied into this folder, and BijouHub will read/write there from now on.\n\nRestart now to apply?",
            "Sync Folder", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (restart != MessageBoxResult.Yes) return;

        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
            System.Diagnostics.Process.Start(exePath);
        Application.Current.Shutdown();
    }

    // ---------- Zoom ----------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            HomePanel.Visibility == Visibility.Visible && GoalScopeTabs.Visibility == Visibility.Visible)
        {
            CycleGoalScope(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }

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

        if (para.Inlines.FirstInline is InlineUIContainer { Child: Border })
        {
            NotesRichBox.Focus();
            return; // line already has a checklist marker
        }

        var container = new InlineUIContainer(CreateChecklistMarker(false));
        if (para.Inlines.FirstInline != null)
            para.Inlines.InsertBefore(para.Inlines.FirstInline, container);
        else
            para.Inlines.Add(container);

        NotesRichBox.Focus();
    }

    private Border CreateChecklistMarker(bool isChecked)
    {
        var border = new Border
        {
            Uid = "checklist-marker",
            Width = 18,
            Height = 18,
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(0, 0, 6, -3),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new System.Windows.Media.ScaleTransform(1, 1)
        };
        border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        ApplyChecklistMarkerVisual(border, isChecked);
        return border;
    }

    private void ApplyChecklistMarkerVisual(Border border, bool isChecked)
    {
        var accent = (System.Windows.Media.Color)Application.Current.Resources["AccentColor"];
        border.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(isChecked ? (byte)0x55 : (byte)0x1A, accent.R, accent.G, accent.B));
        border.Effect = ThemeService.IsClassicChrome(ThemeService.CurrentThemeName)
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accent,
                BlurRadius = isChecked ? 10 : 5,
                ShadowDepth = 0,
                Opacity = isChecked ? 0.85 : 0.4
            }
            : null;
        System.Windows.Shapes.Path? check = isChecked
            ? new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M2,7 L7,12 L15,2"),
                StrokeThickness = 2.2,
                StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
                Width = 15,
                Height = 12,
                Stretch = System.Windows.Media.Stretch.Uniform
            }
            : null;
        check?.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
        border.Child = check;
    }

    private void NotesRichBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(NotesRichBox);

        foreach (var block in NotesRichBox.Document.Blocks)
        {
            if (block is not Paragraph para) continue;
            foreach (var inline in para.Inlines)
            {
                if (inline is not InlineUIContainer { Child: Border { Uid: "checklist-marker" } border } container)
                    continue;

                var topLeft = border.TranslatePoint(new Point(0, 0), NotesRichBox);
                var rect = new Rect(topLeft, new Size(border.ActualWidth, border.ActualHeight));
                if (!rect.Contains(point)) continue;

                var newChecked = border.Child is not System.Windows.Shapes.Path;
                ApplyChecklistMarkerVisual(border, newChecked);
                PopAnimate(border);
                StyleChecklistLineFrom(container.ElementEnd, newChecked);
                e.Handled = true;
                return;
            }
        }
    }

    private static void StyleChecklistLineFrom(TextPointer afterMarker, bool complete)
    {
        var para = afterMarker.Paragraph;
        if (para == null) return;

        var muted = (System.Windows.Media.Brush)Application.Current.Resources["MutedTextBrush"];
        var normal = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"];

        var lineRange = new TextRange(afterMarker, para.ContentEnd);
        lineRange.ApplyPropertyValue(TextElement.ForegroundProperty, complete ? muted : normal);
        lineRange.ApplyPropertyValue(Inline.TextDecorationsProperty,
            complete ? TextDecorations.Strikethrough : new TextDecorationCollection());
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

    private void OpenTheme_Click(object sender, RoutedEventArgs e)
    {
        var win = new ThemeWindow(_settingsStore) { Owner = this };
        win.ShowDialog();
    }

    // Theme-dependent visuals that can't follow a theme swap on their own: note text colors
    // (see RefreshNoteColors) and the classic-chrome animations — the pulsing timer glow and
    // the scrolling hazard-stripe progress fill (everything else keys off resources the
    // active control-template set defines). Called at startup and on every theme change.
    public void RefreshThemeChrome()
    {
        RefreshNoteColors();
        ShowBoard(); // status colors come through a converter, so re-resolve them for the new theme

        if (!ThemeService.IsClassicChrome(ThemeService.CurrentThemeName))
        {
            TimerDisplay.SetResourceReference(EffectProperty, "TimerGlow");
            ProjectProgressFill.SetResourceReference(Border.BackgroundProperty, "ProgressFillBrush");
            return;
        }

        SetupAnimatedProgressFill();

        var accent = (System.Windows.Media.Color)Application.Current.Resources["AccentColor"];
        var glow = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.35 };
        TimerDisplay.Effect = glow;
        var pulse = new DoubleAnimation(0.35, 0.8, TimeSpan.FromSeconds(1.6))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, pulse);
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
