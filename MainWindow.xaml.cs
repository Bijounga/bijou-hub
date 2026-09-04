using System.IO;
using System.Linq;
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
    private bool _budgetAlertShown;
    private BudgetAlertWindow? _budgetAlertWindow;
    private DateTime _sessionStart;
    private int _activeSeconds;
    private int _idleSeconds;
    private bool _isIdle;
    private bool _notesVisible = true;
    private TimerPopoutWindow? _timerPopout;

    private bool IsSessionActive => _activeMode != null || _activeProject != null;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Closing += (_, _) =>
        {
            if (NotesPanel.Visibility == Visibility.Visible) SaveFreeformNotes();
            _timerPopout?.Close();
            var currentSettings = _settingsStore.Load();
            currentSettings.ZoomLevel = AppScaleTransform.ScaleX;
            _settingsStore.Save(currentSettings);
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

        _quickLaunchApps = _quickLaunchStore.Load();
        foreach (var app in _quickLaunchApps)
            app.Icon = IconExtractor.GetIcon(app.Path);

        InitNotesToolbar();
        SetupAnimatedProgressFill();
        RefreshQuickLaunchPanel();
        UpdateSyncFolderButtonLabel();
        ShowHome();
        _ = CheckForUpdateAsync();
    }

    private UpdateInfo? _pendingUpdate;

    private async Task CheckForUpdateAsync()
    {
        var update = await UpdateService.CheckForUpdateAsync();
        if (update == null) return;

        _pendingUpdate = update;
        UpdateButton.Content = $"⬆ Update to v{update.Version}";
        UpdateButton.ToolTip = "A new version of BijouHub is available. Click to download and restart.";
        UpdateButton.Visibility = Visibility.Visible;
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
            Brush = (System.Windows.Media.Brush)FindResource("HazardBrush"),
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

        if (!string.IsNullOrEmpty(dlg.Note))
        {
            project.Notes.Add(new ProjectNote { Timestamp = end, Text = dlg.Note });
            _projectStore.Save(_projects);
        }

        SelectProjectAndShowDetail(project);
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
        if (NotesPanel.Visibility == Visibility.Visible)
            SaveFreeformNotes();

        EmptyState.Visibility = Visibility.Collapsed;
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

        var label = _notesVisible ? "Hide Notes" : "Notes";
        if (NotesToggleButton != null) NotesToggleButton.Content = label;
        if (ProjectNotesToggleButton != null) ProjectNotesToggleButton.Content = label;
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

    private void PopoutToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_timerPopout != null)
        {
            _timerPopout.Close();
            return;
        }

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
            PopoutToggleButton.Content = "Pop Out";
        };

        _timerPopout = popout;
        PopoutToggleButton.Content = "Dock";
        popout.Show();
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

        _detailProject = null;
        UpdateNotesPanelVisibility();

        var greeting = Greetings.Random();
        TypewriterReveal(HomeWelcomeText, "Welcome back", msPerChar: 8);
        TypewriterReveal(HomeGreetingText, greeting, msPerChar: 8);

        var todaySeconds = _logService.GetTodayTotalSeconds();
        HomeTodayText.Text = FormatSpan(todaySeconds);

        var last7 = _logService.GetLastNDaysTotals(7);
        HomeWeekText.Text = "Last 7 days: " + string.Join("   ", last7.Select(kv => $"{kv.Key:ddd} {FormatSpan(kv.Value)}"));

        HomeProjectsList.ItemsSource = _projects;
    }

    private void HomeHeader_Click(object sender, MouseButtonEventArgs e)
    {
        ModesList.SelectedItem = null;
        ProjectsList.SelectedItem = null;
        ShowHome();
    }

    private void HomeProjectRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Project project }) return;

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
        stack.Children.Add(new TextBlock
        {
            Text = app.Name,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 84,
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush")
        });

        var button = new Button
        {
            Style = (Style)FindResource("QuickLaunchTileStyle"),
            Content = stack,
            Tag = app,
            ToolTip = app.Name
        };
        button.Click += QuickLaunchTile_Click;

        var removeItem = new MenuItem { Header = "Remove" };
        removeItem.Click += (_, _) => RemoveQuickLaunchApp(app);
        button.ContextMenu = new ContextMenu { Items = { removeItem } };

        return button;
    }

    private Button BuildAddQuickLaunchTile()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "+",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 0, 0, 2)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Add App",
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush")
        });

        var button = new Button
        {
            Style = (Style)FindResource("QuickLaunchTileStyle"),
            Content = stack,
            ToolTip = "Add an app to Quick Launch"
        };
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

        TypewriterReveal(ActiveModeName, project?.Name ?? mode?.Name ?? "Session");
        var contextParts = new List<string>();
        if (project != null && mode != null) contextParts.Add($"via {mode.Name}");
        if (goal != null) contextParts.Add($"working on: {goal.Name}");
        if (targetMinutes is int tm) contextParts.Add($"budget: {tm} min");
        ActiveContextText.Text = string.Join("  •  ", contextParts);
        ActiveContextText.Visibility = contextParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ActiveStatus.Text = "Active";
        TimerDisplay.Text = "00:00:00";

        ShowActiveSessionPanel();

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

        if (ActiveSessionBanner.Visibility == Visibility.Visible)
            ActiveSessionBannerText.Text = TimerDisplay.Text;

        _timerPopout?.UpdateDisplay(_activeProject?.Name ?? _activeMode?.Name ?? "Session", TimerDisplay.Text, ActiveStatus.Text);

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
        _timerPopout?.Close();
        _timerPopout = null;

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

    // ---------- Sync folder ----------

    private void UpdateSyncFolderButtonLabel()
    {
        var settings = _settingsStore.Load();
        if (string.IsNullOrEmpty(settings.DataFolderPath))
        {
            SyncFolderButton.Content = "Sync Folder...";
            SyncFolderButton.ToolTip = "Point Projects and session history at a folder you sync across devices";
        }
        else
        {
            SyncFolderButton.Content = "🔗 Synced";
            SyncFolderButton.ToolTip = $"Syncing via: {settings.DataFolderPath}\nClick to change";
        }
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
            BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 0, 6, -3),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new System.Windows.Media.ScaleTransform(1, 1)
        };
        ApplyChecklistMarkerVisual(border, isChecked);
        return border;
    }

    private void ApplyChecklistMarkerVisual(Border border, bool isChecked)
    {
        border.Background = isChecked
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0x33, 0xE1, 0xFF))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0x33, 0xE1, 0xFF));
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#33E1FF"),
            BlurRadius = isChecked ? 10 : 5,
            ShadowDepth = 0,
            Opacity = isChecked ? 0.85 : 0.4
        };
        border.Child = isChecked
            ? new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M2,7 L7,12 L15,2"),
                Stroke = (System.Windows.Media.Brush)FindResource("AccentBrush"),
                StrokeThickness = 2.2,
                StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
                Width = 15,
                Height = 12,
                Stretch = System.Windows.Media.Stretch.Uniform
            }
            : null;
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
