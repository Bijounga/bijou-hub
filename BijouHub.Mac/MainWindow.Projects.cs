using Avalonia.Controls;
using Avalonia.Interactivity;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Services.GoogleTasks;

namespace BijouHub.Mac;

// Projects: goals with weights, time spent, session notes, plain-text notes (shared with Windows).
public partial class MainWindow
{
    private void ShowProject(Project project)
    {
        _detailProject = project;
        ShowOnly(ProjectPanel);
        ProjectNameText.Text = project.Name;
        RefreshProjectProgress(project);
        ProjectGoals.ItemsSource = null;
        ProjectGoals.ItemsSource = project.Goals;
        ProjectNotesList.ItemsSource = project.Notes.OrderByDescending(n => n.Timestamp).ToList();
        ProjectNotesLabel.IsVisible = project.Notes.Count > 0;
        var seconds = _logService.GetForProject(project.Id).Sum(s => s.ActiveSeconds);
        ProjectTimeText.Text = $"Time spent: {FormatSpan(seconds)}";
    }

    private void RefreshProjectProgress(Project project)
    {
        ProjectProgressBar.Value = Math.Clamp(project.Completion, 0, 1);
        var next = project.NextIncompleteGoal();
        ProjectProgressText.Text = $"{project.CompletionPercentText} complete" + (next != null ? $" — Next: {next.Name}" : "");
    }

    private void ProjectGoal_Click(object? sender, RoutedEventArgs e)
    {
        if (_detailProject == null) return;
        _projectStore.Save(_projects.ToList());
        RefreshProjectProgress(_detailProject);
        ProjectGoals.ItemsSource = null;
        ProjectGoals.ItemsSource = _detailProject.Goals; // parent completion figures recompute
        _boardBuiltAt = DateTime.MinValue;
    }

    private void PersistProjects()
    {
        _projectStore.Save(_projects.ToList());
        _boardBuiltAt = DateTime.MinValue;
        ReloadChannels(); // the project editor can add channels
        RebuildProjectRows();
    }

    private async void NewProject_Click(object? sender, RoutedEventArgs e)
    {
        var project = new Project();
        if (!await new ProjectEditorWindow(project).ShowDialog<bool>(this)) return;
        _projects.Add(project);
        PersistProjects();
        SelectProject(project);
    }

    // Edits a copy so Cancel really cancels (the editor changes goals as you type).
    private async void EditProject_Click(object? sender, RoutedEventArgs e)
    {
        if (_detailProject is not Project project) return;
        var copy = project.Clone();
        if (!await new ProjectEditorWindow(copy).ShowDialog<bool>(this)) return;
        project.Name = copy.Name;
        project.Goals = copy.Goals;
        project.DefaultTargetMinutes = copy.DefaultTargetMinutes;
        project.LinkedModeId = copy.LinkedModeId;
        project.ChannelId = copy.ChannelId;
        PersistProjects();
        ShowProject(project);
    }

    private async void DeleteProject_Click(object? sender, RoutedEventArgs e)
    {
        if (_detailProject is not Project project) return;

        // With Google Tasks, the project has a list there too ("EDITING - name"): offer to delete it with it.
        var deleteList = false;
        if (GoogleMode && _googleSync?.HasList(EditingGroup, project.Name) == true)
        {
            var open = _dailyGoals.Count(g => !g.Done && GoogleGoalsSync.GroupOf(g) == EditingGroup
                                              && string.Equals(g.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase));
            var tasks = open switch { 0 => "", 1 => " (1 open task)", _ => $" ({open} open tasks)" };
            var choice = await PromptWindow.Choose(this, "Delete project",
                $"Delete \"{project.Name}\"? Its logged time stays in the session log.\n\nIt also has a list in Google Tasks, EDITING - {project.Name}{tasks}. " +
                "Delete that too, or keep it (it stays under Editing on the Tasks page and on your phone)?",
                "Delete project only", "Delete project and its list");
            if (choice < 0) return;
            deleteList = choice == 1;
        }
        else if (!await PromptWindow.Confirm(this, "Delete project", $"Delete the project \"{project.Name}\"? Its logged time stays in the session log.", "Delete"))
        {
            return;
        }

        _projects.Remove(project);
        PersistProjects();
        ProjectsList.SelectedItem = null;
        ShowHome();
        if (deleteList) await DeleteGoogleListAsync(new ListTarget(EditingGroup, project.Name, project.Id, project.Name), askFirst: false);
    }

    private async void LogTime_Click(object? sender, RoutedEventArgs e)
    {
        if (_detailProject is not Project project) return;
        var dialog = new LogTimeWindow();
        if (!await dialog.ShowDialog<bool>(this) || dialog.TotalMinutes <= 0) return;

        var end = DateTime.Now;
        _logService.InsertSession(new SessionRecord
        {
            ModeName = "Manual",
            StartTime = end.AddMinutes(-dialog.TotalMinutes),
            EndTime = end,
            ActiveSeconds = dialog.TotalMinutes * 60,
            ProjectId = project.Id,
            ProjectName = project.Name,
            Note = dialog.Note
        });
        if (!string.IsNullOrEmpty(dialog.Note))
        {
            project.Notes.Add(new ProjectNote { Timestamp = end, Text = dialog.Note });
            _projectStore.Save(_projects.ToList());
        }
        InvalidateTodayLogged();
        ShowProject(project);
    }

    private async void StartProjectSession_Click(object? sender, RoutedEventArgs e)
    {
        if (_detailProject is not Project project) return;
        var dialog = new StartSessionWindow(project, _modes.ToList());
        if (!await dialog.ShowDialog<bool>(this)) return;
        await BeginSession(dialog.SelectedMode, project, dialog.SelectedGoal, dialog.TargetMinutes, dialog.CountDown);
    }
}
