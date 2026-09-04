using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BijouHub.Mac.Services;
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

    private DateTime? _sessionStart;
    private DispatcherTimer? _tickTimer;

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

    private void AddGoal_Click(object? sender, RoutedEventArgs e)
    {
        if (_activeProject == null) return;
        var name = NewGoalNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        _activeProject.Goals.Add(new Goal { Name = name, Weight = 100 });
        NewGoalNameBox.Text = "";
        SaveProjects();
        RefreshProjectPanel();
    }

    private void GoalCheck_Click(object? sender, RoutedEventArgs e)
    {
        SaveProjects();
        RefreshProjectPanel();
    }

    private void SaveProjects() => _projectStore.Save(_projects.ToList());

    private void SessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_sessionStart == null)
            StartSession();
        else
            StopSession();
    }

    private void StartSession()
    {
        _sessionStart = DateTime.Now;
        SessionButton.Content = "Stop Session";
        SessionButton.Classes.Remove("accent");

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _sessionStart!.Value;
            ElapsedText.Text = elapsed.ToString(@"hh\:mm\:ss");
        };
        _tickTimer.Start();
    }

    private void StopSession()
    {
        if (_sessionStart == null || _activeProject == null) return;

        _tickTimer?.Stop();
        var start = _sessionStart.Value;
        var end = DateTime.Now;
        var activeSeconds = (int)(end - start).TotalSeconds;

        var note = SessionNoteBox.Text?.Trim();

        _sessionLogService.InsertSession(new SessionRecord
        {
            ModeName = "Mac",
            StartTime = start,
            EndTime = end,
            ActiveSeconds = activeSeconds,
            IdleSeconds = 0,
            ProjectId = _activeProject.Id,
            ProjectName = _activeProject.Name,
            Note = string.IsNullOrEmpty(note) ? null : note
        });

        if (!string.IsNullOrEmpty(note))
        {
            _activeProject.Notes.Add(new ProjectNote { Timestamp = end, Text = note });
            SaveProjects();
        }

        SessionNoteBox.Text = "";
        _sessionStart = null;
        SessionButton.Content = "Start Session";
        SessionButton.Classes.Add("accent");
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
        HomePanel.IsVisible = true;
        ProjectPanel.IsVisible = false;

        UpdateSyncFolderButtonLabel();
        UpdateHomeStats();
    }
}
