using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BijouHub.Mac.Services;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

public partial class MainWindow : Window
{
    private ProjectStore _projectStore = new();
    private SessionLogService _sessionLogService = new();
    private readonly AppSettingsStore _settingsStore = new();

    private ObservableCollection<Project> _projects = new();
    private Project? _activeProject;
    private Project? _notesLoadedFor;

    private DateTime? _sessionStart;
    private Goal? _sessionGoal;
    private int? _sessionTargetMinutes;
    private DispatcherTimer? _tickTimer;

    private bool _notesVisible = true;

    private MacUpdateInfo? _pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();

        _projects = new ObservableCollection<Project>(_projectStore.Load());
        ProjectsList.ItemsSource = _projects;

        UpdateSyncFolderButtonLabel();
        UpdateHomeStats();

        VersionButton.Content = $"v{MacUpdateService.GetCurrentVersion()}";
        _ = CheckForUpdateAsync(silent: true);
        _ = Task.Run(MacUpdateService.EjectStaleMounts);

        Closing += (_, _) => SaveFreeformNotesIfLoaded();
    }

    private async Task CheckForUpdateAsync(bool silent)
    {
        if (!silent)
        {
            VersionButton.Content = "Checking...";
            VersionButton.IsEnabled = false;
        }

        MacUpdateInfo? update = null;
        try
        {
            update = await MacUpdateService.CheckForUpdateAsync();
        }
        catch
        {
            // No network, rate-limited, etc. — silently skip.
        }

        _pendingUpdate = update;
        if (update != null)
        {
            UpdateButton.IsVisible = true;
            UpdateButton.Content = $"⬆ Update to v{update.Version}";
        }
        else
        {
            UpdateButton.IsVisible = false;
        }

        if (!silent)
            VersionButton.IsEnabled = true;
        VersionButton.Content = $"v{MacUpdateService.GetCurrentVersion()}";
    }

    private void VersionButton_Click(object? sender, RoutedEventArgs e) => _ = CheckForUpdateAsync(silent: false);

    private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;

        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "Downloading...";
        try
        {
            await MacUpdateService.DownloadAndOpenAsync(_pendingUpdate.DownloadUrl);
            UpdateButton.Content = "Opened DMG — drag to Applications";
        }
        catch
        {
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = "Update failed — retry?";
        }
    }

    private void UpdateHomeStats()
    {
        var todaySeconds = _sessionLogService.GetTodayTotalSeconds();
        TodayTotalText.Text = $"Today: {FormatMinutes(todaySeconds)}";
    }

    private static string FormatMinutes(int totalSeconds) => $"{totalSeconds / 60}m";

    private void ProjectsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        SaveFreeformNotesIfLoaded();

        _activeProject = ProjectsList.SelectedItem as Project;
        if (_activeProject == null)
        {
            HomePanel.IsVisible = true;
            ProjectPanel.IsVisible = false;
            return;
        }

        HomePanel.IsVisible = false;
        ProjectPanel.IsVisible = true;
        RefreshProjectPanel();
        LoadFreeformNotes(_activeProject);
        UpdateNotesPanelVisibility();
    }

    private void RefreshProjectPanel()
    {
        if (_activeProject == null) return;

        ProjectNameText.Text = _activeProject.Name;
        ProjectProgressBar.Value = _activeProject.Completion;
        ProjectProgressText.Text = _activeProject.CompletionPercentText;
        GoalsList.ItemsSource = _activeProject.Goals;
        NotesList.ItemsSource = _activeProject.Notes.OrderByDescending(n => n.Timestamp).ToList();
    }

    private void NewProject_Click(object? sender, RoutedEventArgs e)
    {
        var project = new Project { Name = "New Project" };
        _projects.Add(project);
        _projectStore.Save(_projects.ToList());
        ProjectsList.SelectedItem = project;
    }

    private async void EditProject_Click(object? sender, RoutedEventArgs e)
    {
        if (_activeProject == null) return;

        var editor = new ProjectEditorWindow(_activeProject);
        var saved = await editor.ShowDialog<bool>(this);
        if (!saved) return;

        SaveProjects();
        // Force the sidebar list and detail panel to pick up the (possibly renamed) project.
        var project = _activeProject;
        ProjectsList.ItemsSource = null;
        ProjectsList.ItemsSource = _projects;
        ProjectsList.SelectedItem = project;
        RefreshProjectPanel();
    }

    private async void LogTime_Click(object? sender, RoutedEventArgs e)
    {
        if (_activeProject == null) return;

        var dlg = new LogTimeWindow();
        var logged = await dlg.ShowDialog<bool>(this);
        if (!logged) return;

        var end = DateTime.Now;
        var start = end.AddMinutes(-dlg.TotalMinutes);

        _sessionLogService.InsertSession(new SessionRecord
        {
            ModeName = "Manual",
            StartTime = start,
            EndTime = end,
            ActiveSeconds = dlg.TotalMinutes * 60,
            IdleSeconds = 0,
            ProjectId = _activeProject.Id,
            ProjectName = _activeProject.Name,
            Note = dlg.Note
        });

        if (!string.IsNullOrEmpty(dlg.Note))
        {
            _activeProject.Notes.Add(new ProjectNote { Timestamp = end, Text = dlg.Note });
            SaveProjects();
        }

        RefreshProjectPanel();
        UpdateHomeStats();
    }

    private void DeleteProject_Click(object? sender, RoutedEventArgs e)
    {
        if (_activeProject == null) return;

        _projects.Remove(_activeProject);
        SaveProjects();
        _activeProject = null;
        HomePanel.IsVisible = true;
        ProjectPanel.IsVisible = false;
    }

    private void GoalCheck_Click(object? sender, RoutedEventArgs e)
    {
        SaveProjects();
        RefreshProjectPanel();
    }

    private void SaveProjects() => _projectStore.Save(_projects.ToList());

    // ---------- Freeform notes ----------

    private void UpdateNotesPanelVisibility()
    {
        NotesPanel.IsVisible = _notesVisible;
        NotesToggleButton.Content = _notesVisible ? "Hide Notes" : "Show Notes";
    }

    private void NotesToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (_notesVisible) SaveFreeformNotesIfLoaded();
        _notesVisible = !_notesVisible;
        UpdateNotesPanelVisibility();
    }

    private void LoadFreeformNotes(Project project)
    {
        FreeformNotesBox.Text = project.NotesPlainText ?? "";
        _notesLoadedFor = project;
    }

    private void SaveFreeformNotesIfLoaded()
    {
        if (_notesLoadedFor == null) return;
        _notesLoadedFor.NotesPlainText = FreeformNotesBox.Text ?? "";
        SaveProjects();
    }

    private async void SessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_sessionStart == null)
            await StartSession();
        else
            await StopSession();
    }

    private async Task StartSession()
    {
        if (_activeProject == null) return;

        var dlg = new StartSessionWindow(_activeProject);
        var started = await dlg.ShowDialog<bool>(this);
        if (!started) return;

        _sessionGoal = dlg.SelectedGoal;
        _sessionTargetMinutes = dlg.TargetMinutes;

        _sessionStart = DateTime.Now;
        SessionButton.Content = "Stop Session";
        SessionButton.Classes.Remove("accent");
        SessionSubtitleText.Text = _sessionGoal != null ? $"// SESSION — {_sessionGoal.Name}" : "// SESSION";

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _sessionStart!.Value;
            ElapsedText.Text = elapsed.ToString(@"hh\:mm\:ss");
            if (_sessionTargetMinutes is int budget)
            {
                var remaining = budget - (int)elapsed.TotalMinutes;
                SessionSubtitleText.Text = (_sessionGoal != null ? $"// SESSION — {_sessionGoal.Name} — " : "// SESSION — ")
                    + (remaining >= 0 ? $"{remaining}m left of {budget}m" : $"{-remaining}m over {budget}m budget");
            }
        };
        _tickTimer.Start();
    }

    private async Task StopSession()
    {
        if (_sessionStart == null || _activeProject == null) return;

        _tickTimer?.Stop();
        var start = _sessionStart.Value;
        var end = DateTime.Now;
        var activeSeconds = (int)(end - start).TotalSeconds;

        var finishDlg = new FinishNoteWindow();
        await finishDlg.ShowDialog<bool>(this);
        var note = finishDlg.Note;

        _sessionLogService.InsertSession(new SessionRecord
        {
            ModeName = "Mac",
            StartTime = start,
            EndTime = end,
            ActiveSeconds = activeSeconds,
            IdleSeconds = 0,
            ProjectId = _activeProject.Id,
            ProjectName = _activeProject.Name,
            GoalId = _sessionGoal?.Id,
            GoalName = _sessionGoal?.Name,
            Note = note
        });

        if (!string.IsNullOrEmpty(note))
        {
            _activeProject.Notes.Add(new ProjectNote { Timestamp = end, Text = note });
            SaveProjects();
        }

        _sessionStart = null;
        _sessionGoal = null;
        _sessionTargetMinutes = null;
        SessionButton.Content = "▶ Start Session";
        SessionButton.Classes.Add("accent");
        SessionSubtitleText.Text = "// SESSION";
        ElapsedText.Text = "00:00:00";

        RefreshProjectPanel();
        UpdateHomeStats();
    }

    private void UpdateSyncFolderButtonLabel()
    {
        var settings = _settingsStore.Load();
        SyncFolderButton.Content = string.IsNullOrEmpty(settings.DataFolderPath) ? "Sync Folder..." : "🔗 Synced";
    }

    private async void SyncFolder_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to sync Projects & session history through",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;

        var chosen = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(chosen)) return;

        var hasExistingSyncedData = File.Exists(Path.Combine(chosen, "projects.json"))
            || File.Exists(Path.Combine(chosen, "sessions.json"));

        if (!hasExistingSyncedData)
        {
            foreach (var fileName in new[] { "projects.json", "sessions.json" })
            {
                var source = Path.Combine(DataPaths.SyncDir, fileName);
                var dest = Path.Combine(chosen, fileName);
                if (File.Exists(source) && !File.Exists(dest))
                    File.Copy(source, dest);
            }
        }

        var settings = _settingsStore.Load();
        settings.DataFolderPath = chosen;
        _settingsStore.Save(settings);

        // Re-create the stores so they pick up the new folder immediately, no restart needed.
        _projectStore = new ProjectStore();
        _sessionLogService = new SessionLogService();

        _projects = new ObservableCollection<Project>(_projectStore.Load());
        ProjectsList.ItemsSource = _projects;
        _activeProject = null;
        _notesLoadedFor = null;
        HomePanel.IsVisible = true;
        ProjectPanel.IsVisible = false;

        UpdateSyncFolderButtonLabel();
        UpdateHomeStats();
    }
}
