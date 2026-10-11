using Avalonia.Controls;
using BijouHub.Mac.Views;
using BijouHub.Models;

namespace BijouHub.Mac;

// "What am I working on?": the task picked for the running session (under the timer, in the main
// window and the pop-out). It's recorded with the session, so the history and the calendar can say
// what each stretch of time went to. Same behavior as Windows.
public partial class MainWindow
{
    private DailyGoal? _activeTask;

    private void InitSessionTask()
    {
        SessionTaskPicker.TaskPicked += SessionTask_Picked;
        SessionTaskPicker.TaskAdded += SessionTask_Added;
        SessionTaskPicker.DoneClicked += SessionTask_Done;
    }

    // Today's open tasks, whichever tab or list they're on.
    private List<DailyGoal> OpenTasksToday() => _dailyGoals.Where(g => !g.Done && !g.IsPlannedAfter(TodayKey)).ToList();

    private void RefreshSessionTaskPickers()
    {
        if (!IsSessionActive)
        {
            _activeTask = null;
            return;
        }
        // A task finished or deleted elsewhere (the list, the phone) is no longer the one being worked on.
        if (_activeTask != null && (_activeTask.Done || !_dailyGoals.Contains(_activeTask))) _activeTask = null;
        var tasks = OpenTasksToday();
        SessionTaskPicker.SetTasks(tasks, _activeTask);
        _popout?.SetTasks(tasks, _activeTask);
    }

    private void SessionTask_Picked(DailyGoal? goal)
    {
        _activeTask = goal;
        RefreshSessionTaskPickers();
    }

    // A new task added from the timer: it goes on today's list (in the session's project, if there is one) and becomes the picked one.
    private void SessionTask_Added(string text)
    {
        LoadDailyPlan(); // past midnight, it belongs to the new day
        var goal = new DailyGoal { Text = text, Group = EditingGroup, ProjectId = _activeProject?.Id, ProjectName = _activeProject?.Name };
        AddDailyGoal(goal);
        _activeTask = goal;
        UpdateDailyProgress();
        RefreshSessionTaskPickers();
    }

    private void SessionTask_Done()
    {
        if (_activeTask == null) return;
        _activeTask.Done = true;
        _activeTask = null;
        RefreshSessionTaskPickers();
    }

    private void WireSessionTaskPopout(TimerPopoutWindow popout)
    {
        popout.TaskPicked += SessionTask_Picked;
        popout.TaskAdded += SessionTask_Added;
        popout.TaskDone += SessionTask_Done;
        popout.SetTasks(OpenTasksToday(), _activeTask);
    }
}
