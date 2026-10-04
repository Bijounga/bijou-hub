using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BijouHub.Mac.Services;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// The Mac BijouHub: the same features as Windows (modes and timers, projects, sessions, today's
// goals with Google Tasks, the project board, the Stream Deck) minus app blocking. Split into
// partial files by area; this one is the shell — startup, navigation and the sidebar tools.
public partial class MainWindow : Window
{
    private readonly ModeStore _modeStore = new();
    private ProjectStore _projectStore = new();
    private SessionLogService _logService = new();
    private readonly AppSettingsStore _settingsStore = new();

    private readonly ObservableCollection<WorkMode> _modes;
    private ObservableCollection<Project> _projects;
    private WorkMode? _shownMode;
    private Project? _detailProject;
    private bool _notesVisible = true;

    public MainWindow()
    {
        InitializeComponent();

        RecoverInterruptedSession();
        MacDoNotDisturb.Restore(); // left on by a session that didn't end cleanly
        var savedSettings = new AppSettingsStore().Load();
        _dailyTargetMinutes = savedSettings.DailyTargetMinutes;
        _showCompleted = savedSettings.ShowCompletedGoals;
        _modes = new ObservableCollection<WorkMode>(_modeStore.Load());
        ModesList.ItemsSource = _modes;
        _projects = new ObservableCollection<Project>(_projectStore.Load());
        InitChannels();

        VersionText.Text = "v" + MacUpdateService.GetCurrentVersion();
        InitSession();
        InitSidebarMenus();
        InitDue();
        InitGoals();
        InitTasksPage();
        InitGoogleTasks();
        InitDeck();
        InitTray();
        MacPlatform.UpgradeLoginItem();
        RefreshToolStates();
        ShowHome();

        Opened += async (_, _) =>
        {
            await OfferMoveToApplicationsAsync();
            _ = CheckForUpdateAsync();
            // BijouHub tends to stay open for days on a Mac, so look again every few hours.
            var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            updateTimer.Tick += (_, _) => { if (_pendingUpdate == null) _ = CheckForUpdateAsync(); };
            updateTimer.Start();
            _ = Task.Run(MacUpdateService.TidyOnLaunch);
        };
        Closing += (_, e) =>
        {
            // Kept running for the Stream Deck: the close button hides it to the menu bar. Quit
            // (Cmd+Q or the menu), an update or a logout still close it.
            if (_keepRunning && !_quitting && e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            if (_tray != null) _tray.IsVisible = false;

            SaveProjectNotes();
            SaveDailyPlanNow();
            _dailyStore.Flush();
            _deckBridge?.Dispose();
            _popout?.Close();
            if (IsSessionActive) FinalizeSessionSilently();
        };
    }

    // ---------- Navigation ----------

    // Right-clicking a mode or project opens it (like a click) and then its Edit / Delete menu.
    private void InitSidebarMenus()
    {
        foreach (var list in new[] { ModesList, ProjectsList })
        {
            list.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(list).Properties.IsRightButtonPressed) return;
                var item = (e.Source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
                if (item != null && item.DataContext is not ChannelHeader) list.SelectedItem = item.DataContext;
                _sidebarMenuOnItem = item != null;
                _sidebarMenuRow = item?.DataContext;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }
    }

    private bool _sidebarMenuOnItem;

    // Only over an item: right-clicking the empty part of the list shows nothing.
    private void SidebarMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_sidebarMenuOnItem) e.Cancel = true;
    }

    private void ShowOnly(Control panel)
    {
        SaveProjectNotes();
        HomePanel.IsVisible = panel == HomePanel;
        TasksPanel.IsVisible = panel == TasksPanel;
        NavHomeButton.Classes.Set("on", panel == HomePanel);
        NavTasksButton.Classes.Set("on", panel == TasksPanel);
        ModePanel.IsVisible = panel == ModePanel;
        ProjectPanel.IsVisible = panel == ProjectPanel;
        SessionPanel.IsVisible = panel == SessionPanel;
        UpdateNotesPanel();
    }

    private void Logo_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ModesList.SelectedItem = null;
        ProjectsList.SelectedItem = null;
        ShowHome();
    }

    private void ModesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ModesList.SelectedItem is not WorkMode mode)
        {
            if (ProjectsList.SelectedItem == null && !IsSessionActive) ShowHome();
            return;
        }
        ProjectsList.SelectedItem = null;
        if (mode == _activeMode && IsSessionActive) ShowSession();
        else ShowMode(mode);
    }

    private void ProjectsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_rebuildingProjectRows) return;

        // A channel header isn't a project: keep showing what was open.
        if (ProjectsList.SelectedItem is ChannelHeader)
        {
            _rebuildingProjectRows = true;
            ProjectsList.SelectedItem = _detailProject != null && ProjectsList.Items.Contains(_detailProject) ? _detailProject : null;
            _rebuildingProjectRows = false;
            return;
        }

        if (ProjectsList.SelectedItem is not Project project)
        {
            if (ModesList.SelectedItem == null && !IsSessionActive) ShowHome();
            return;
        }
        ModesList.SelectedItem = null;
        if (project == _activeProject && IsSessionActive) ShowSession();
        else ShowProject(project);
    }

    private void SelectProject(Project project)
    {
        ModesList.SelectedItem = null;
        if (ProjectsList.SelectedItem == project) ShowProject(project);
        else ProjectsList.SelectedItem = project;
    }

    // ---------- Notes panel (project notes, shown beside a project or its running session) ----------

    private Project? _notesLoadedFor;

    private void UpdateNotesPanel()
    {
        var project = ProjectPanel.IsVisible ? _detailProject : SessionPanel.IsVisible ? _activeProject : null;
        var show = _notesVisible && project != null;
        if (show && _notesLoadedFor != project)
        {
            NotesBox.Text = project!.NotesPlainText ?? "";
            _notesLoadedFor = project;
        }
        NotesPanel.IsVisible = show;
        var tip = _notesVisible ? "Hide notes" : "Show notes";
        ToolTip.SetTip(ProjectNotesToggle, tip);
        ToolTip.SetTip(SessionNotesToggle, tip);
    }

    private void NotesToggle_Click(object? sender, RoutedEventArgs e)
    {
        SaveProjectNotes();
        _notesVisible = !_notesVisible;
        UpdateNotesPanel();
    }

    private void NotesBox_LostFocus(object? sender, RoutedEventArgs e) => SaveProjectNotes();

    private void SaveProjectNotes()
    {
        if (_notesLoadedFor == null) return;
        var text = NotesBox.Text ?? "";
        if (text == (_notesLoadedFor.NotesPlainText ?? "")) return;
        _notesLoadedFor.NotesPlainText = text;
        // The Windows app's rich notes win on Windows; clearing them hands this text over there too.
        _notesLoadedFor.FreeformNotesXaml = null;
        _projectStore.Save(_projects.ToList());
    }

    // ---------- Sidebar tools ----------

    // Launched at login with "keep running in the background" on: straight to the menu bar.
    public void StartInTray()
    {
        ShowTrayIcon();
        Opacity = 0;
        EventHandler? hide = null;
        hide = (_, _) =>
        {
            Opened -= hide;
            Hide();
            Opacity = 1;
        };
        Opened += hide;
    }

    private void RefreshToolStates()
    {
        KeepRunningButton.Classes.Set("on", _keepRunning);
        ToolTip.SetTip(KeepRunningButton, _keepRunning
            ? "Closing hides BijouHub in the menu bar, so the Stream Deck keeps working. Click to turn off; right-click to quit."
            : "Keep running in the background when closed (for the Stream Deck)");

        var synced = !string.IsNullOrEmpty(_settingsStore.Load().DataFolderPath);
        SyncFolderButton.Classes.Set("on", synced);
        ToolTip.SetTip(SyncFolderButton, synced
            ? $"Syncing projects and history via {_settingsStore.Load().DataFolderPath} — click to change"
            : "Sync folder: keep projects and history in a folder you sync across devices");

        var login = MacPlatform.StartsAtLogin;
        LoginItemButton.Classes.Set("on", login);
        ToolTip.SetTip(LoginItemButton, login ? "Opens at login — click to turn off" : "Open at login");
    }

    private async void SessionLog_Click(object? sender, RoutedEventArgs e)
    {
        var window = new SessionLogWindow(_logService, _projects.ToList());
        await window.ShowDialog(this);
        if (!window.AssignmentsChanged) return;
        _boardBuiltAt = DateTime.MinValue;
        if (ProjectPanel.IsVisible && _detailProject != null) ShowProject(_detailProject);
        else if (HomePanel.IsVisible) ShowHome();
    }

    private async void SyncFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to sync projects, goals and history through",
            AllowMultiple = false
        });
        var chosen = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(chosen)) return;

        var hasData = File.Exists(Path.Combine(chosen, "projects.json")) || File.Exists(Path.Combine(chosen, "sessions.json"));
        if (!hasData)
        {
            // First time pointing here: bring the existing data along so nothing's lost.
            foreach (var file in new[] { "projects.json", "sessions.json", "daily.json" })
            {
                var source = Path.Combine(DataPaths.SyncDir, file);
                var target = Path.Combine(chosen, file);
                if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target);
            }
        }

        _dailyStore.Flush();
        var settings = _settingsStore.Load();
        settings.DataFolderPath = chosen;
        _settingsStore.Save(settings);

        // Re-create the stores so they read the new folder straight away — no restart needed.
        _projectStore = new ProjectStore();
        _channelStore = new ChannelStore();
        _logService = new SessionLogService();
        _dailyStore = new DailyPlanStore();
        _today = null;
        _projects = new ObservableCollection<Project>(_projectStore.Load());
        ReloadChannels();
        RebuildProjectRows();
        InvalidateTodayLogged();
        RefreshToolStates();
        ShowHome();
    }

    private void LoginItem_Click(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsMacOS()) return;
        MacPlatform.SetStartsAtLogin(!MacPlatform.StartsAtLogin);
        RefreshToolStates();
    }

    private async void Theme_Click(object? sender, RoutedEventArgs e)
    {
        await new ThemeWindow(_settingsStore).ShowDialog(this);
        _boardBuiltAt = DateTime.MinValue; // card colors come from the theme
        if (HomePanel.IsVisible) ShowBoard();
    }

    private async void StreamDeck_Click(object? sender, RoutedEventArgs e) =>
        await new StreamDeckWindow(() => _deckBridge?.ClientCount ?? 0).ShowDialog(this);

    // ---------- Updates ----------

    private MacUpdateInfo? _pendingUpdate;
    private string? _downloadedUpdate;

    // False when the check couldn't reach GitHub.
    private async Task<bool> CheckForUpdateAsync()
    {
        try
        {
            _pendingUpdate = await MacUpdateService.CheckForUpdateAsync();
        }
        catch
        {
            return false; // offline or rate-limited — try again later
        }
        if (_pendingUpdate == null) return true;

        UpdateButton.Content = $"Update to v{_pendingUpdate.Version}";
        ToolTip.SetTip(UpdateButton, MacUpdateService.CanSelfUpdate
            ? "Downloads the update, then restarts BijouHub on the new version"
            : "Move BijouHub into Applications to update in place");
        UpdateButton.IsVisible = true;
        return true;
    }

    private bool _checkingForUpdates;

    // Clicking the version number checks for an update right now.
    private async void VersionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_checkingForUpdates) return;
        _checkingForUpdates = true;
        VersionText.Text = "checking…";
        var reached = await CheckForUpdateAsync();
        VersionText.Text = !reached ? "can't reach GitHub" : _pendingUpdate != null ? "update ready ↓" : "up to date ✓";
        await Task.Delay(2500);
        VersionText.Text = "v" + MacUpdateService.GetCurrentVersion();
        _checkingForUpdates = false;
    }

    private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        if (!MacUpdateService.CanSelfUpdate)
        {
            await PromptWindow.Notice(this, "Update", "BijouHub can only update itself from your Applications folder. Move it there (drag it in from Finder), open it again, then click Update.");
            return;
        }

        if (IsSessionActive && !await PromptWindow.Confirm(this, "Update",
                "Updating restarts BijouHub, which finishes and logs the session that's running now.", "Finish and update"))
            return;

        UpdateButton.IsEnabled = false;
        try
        {
            if (_downloadedUpdate == null)
            {
                var progress = new Progress<int>(p => UpdateButton.Content = $"Downloading… {p}%");
                _downloadedUpdate = await MacUpdateService.DownloadAsync(_pendingUpdate, progress);
            }
            UpdateButton.Content = "Restarting…";
            MacUpdateService.InstallAndRelaunch(_downloadedUpdate);
            Close();
        }
        catch (Exception ex)
        {
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = $"Update to v{_pendingUpdate.Version}";
            await PromptWindow.Notice(this, "Update didn't finish", ex.Message);
        }
    }

    // Opened straight from the disk image: offer to move into Applications (then the image can
    // be ejected, and updates can install themselves).
    private async Task OfferMoveToApplicationsAsync()
    {
        if (!MacUpdateService.RunningFromDiskImage) return;
        var move = await PromptWindow.Confirm(this, "Move to Applications",
            "BijouHub is running from the disk image. Move it to your Applications folder? Once it's there, the disk image is ejected and updates install themselves.",
            "Move to Applications");
        if (!move) return;
        try
        {
            await MacUpdateService.MoveToApplicationsAsync();
            Close();
        }
        catch (Exception ex)
        {
            await PromptWindow.Notice(this, "Couldn't move it", ex.Message + "\n\nDrag BijouHub into Applications in Finder instead.");
        }
    }
}
