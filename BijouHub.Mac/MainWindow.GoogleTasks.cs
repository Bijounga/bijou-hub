using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BijouHub.Mac.Services;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services.GoogleTasks;

namespace BijouHub.Mac;

// Today's goals through Google Tasks — the same sync as Windows (Core does the work): edits go up
// in order in the background; pulls on opening Home, on coming back to the window, and every
// minute. The refresh token is encrypted with a key kept in the login Keychain.
public partial class MainWindow
{
    private static readonly HttpClient GoogleHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    private GoogleAuth? _googleAuth;
    private GoogleGoalsSync? _googleSync;
    private readonly SemaphoreSlim _googleLock = new(1, 1);
    private readonly HashSet<DailyGoal> _googleCreating = new();
    private int _googlePending;
    private bool _applyingRemote;
    private DateTime _lastGoogleRefresh;
    private DispatcherTimer? _googlePollTimer;

    private enum SyncState { Off, Syncing, Synced, Failed, SignInNeeded }

    private bool GoogleMode => _googleAuth?.IsReady == true;

    private void InitGoogleTasks()
    {
        _googleAuth = new GoogleAuth(GoogleHttp, MacPlatform.Protect, MacPlatform.Unprotect);
        _googleSync = new GoogleGoalsSync(new GoogleTasksClient(GoogleHttp, _googleAuth));

        _googlePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _googlePollTimer.Tick += (_, _) =>
        {
            if (IsActive && HomePanel.IsVisible) _ = RefreshGoogleGoalsAsync();
        };
        _googlePollTimer.Start();

        Activated += (_, _) =>
        {
            if (HomePanel.IsVisible && DateTime.Now - _lastGoogleRefresh > TimeSpan.FromSeconds(15)) _ = RefreshGoogleGoalsAsync();
        };
        SetSyncState(GoogleMode ? SyncState.Syncing : SyncState.Off);
    }

    private void SetSyncState(SyncState state, string? detail = null)
    {
        var (icon, text, brush, tip) = state switch
        {
            SyncState.Syncing => ("sync", "", "MutedTextBrush", "Syncing with Google Tasks…"),
            SyncState.Synced => ("cloud", "", "MutedTextBrush", $"Synced with Google Tasks at {DateTime.Now:t}. Click to manage."),
            SyncState.Failed => ("warning", "Not synced", "HazardBrush", (detail ?? "Couldn't reach Google Tasks") + " — changes will be sent when it's back."),
            SyncState.SignInNeeded => ("warning", "Sign in", "HazardBrush", "Google needs you to sign in again. Click to reconnect."),
            _ => ("cloud", "Sync", "FaintTextBrush", "Sync these goals with Google Tasks — on your phone, the web and other computers.")
        };
        GoogleSyncIcon.Kind = icon;
        GoogleSyncText.Text = text;
        GoogleSyncText.IsVisible = text.Length > 0;
        GoogleSyncButton.Foreground = Brush(brush);
        ToolTip.SetTip(GoogleSyncButton, tip);
    }

    private async void GoogleSync_Click(object? sender, RoutedEventArgs e)
    {
        if (_googleAuth == null) return;
        await new GoogleTasksWindow(_googleAuth).ShowDialog(this);
        if (GoogleMode)
        {
            _ = RefreshGoogleGoalsAsync();
        }
        else
        {
            SetSyncState(SyncState.Off);
            RefreshGoalScope();
            RefreshCarryOver();
            RefreshDailyProjectCombo();
        }
    }

    private async Task RefreshGoogleGoalsAsync()
    {
        if (!GoogleMode || _googleSync == null) return;
        if (_googlePending > 0 || _dailyGoals.Any(g => g.IsEditing)) return;

        SetSyncState(SyncState.Syncing);
        List<DailyGoal> fetched;
        await _googleLock.WaitAsync();
        try
        {
            fetched = await _googleSync.FetchAsync(_projects.Select(p => (p.Id, p.Name)).ToList());
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

        if (_googlePending > 0 || _dailyGoals.Any(g => g.IsEditing)) return;
        LoadDailyPlan();
        ApplyRemoteGoals(fetched);
        _lastGoogleRefresh = DateTime.Now;
        SetSyncState(SyncState.Synced);
        RefreshCarryOver();
        RefreshDailyProjectCombo();
        foreach (var goal in _dailyGoals.Where(g => g.TaskId == null).ToList()) QueueGoogleCreate(goal);
    }

    private void ApplyRemoteGoals(List<DailyGoal> fetched)
    {
        var remoteById = fetched.Where(g => g.TaskId != null).ToDictionary(g => g.TaskId!);
        _applyingRemote = true;
        try
        {
            foreach (var goal in _dailyGoals.ToList())
            {
                if (goal.TaskId == null) continue;
                if (remoteById.Remove(goal.TaskId, out var remote))
                {
                    goal.Text = remote.Text;
                    goal.Group = remote.Group;
                    goal.Starred = remote.Starred;
                    goal.ListId = remote.ListId;
                    goal.ProjectName = remote.ProjectName;
                    goal.ProjectId = remote.ProjectId;
                    goal.CompletedAt = remote.CompletedAt;
                    goal.Done = remote.Done;
                    goal.Due = remote.Due;
                }
                else
                {
                    goal.PropertyChanged -= DailyGoal_PropertyChanged;
                    _dailyGoals.Remove(goal);
                }
            }
            foreach (var remote in fetched.Where(g => g.TaskId != null && remoteById.ContainsKey(g.TaskId)))
            {
                remote.PropertyChanged += DailyGoal_PropertyChanged;
                _dailyGoals.Add(remote);
            }
            PinStarredGoals();
        }
        finally
        {
            _applyingRemote = false;
        }
        RefreshGoalScope();
        SaveDailyPlan();
    }

    private async void QueueGoogle(Func<GoogleGoalsSync, Task> operation)
    {
        if (!GoogleMode || _googleSync == null) return;
        _googlePending++;
        await _googleLock.WaitAsync();
        try
        {
            SetSyncState(SyncState.Syncing);
            await operation(_googleSync);
            SetSyncState(SyncState.Synced);
        }
        catch (Exception ex)
        {
            ReportGoogleError(ex);
        }
        finally
        {
            _googleLock.Release();
            _googlePending--;
            if (_googlePending == 0) SaveDailyPlan();
        }
    }

    private void QueueGoogleCreate(DailyGoal goal)
    {
        if (!_googleCreating.Add(goal)) return;
        QueueGoogle(async sync =>
        {
            try
            {
                if (goal.TaskId != null || !_dailyGoals.Contains(goal)) return;
                await sync.CreateAsync(goal);
                if (!_dailyGoals.Contains(goal)) await sync.DeleteAsync(goal);
            }
            finally
            {
                _googleCreating.Remove(goal);
            }
        });
    }

    private void PushGoalChange(DailyGoal goal, string? property)
    {
        if (!GoogleMode) return;
        if (property is nameof(DailyGoal.Text) or nameof(DailyGoal.Done) or nameof(DailyGoal.Starred) or nameof(DailyGoal.Due))
            QueueGoogle(sync => sync.UpdateAsync(goal));
    }

    private void PushGoalDeleted(DailyGoal goal)
    {
        if (GoogleMode && goal.TaskId != null) QueueGoogle(sync => sync.DeleteAsync(goal));
    }

    private void ReportGoogleError(Exception ex)
    {
        switch (ex)
        {
            case GoogleSignInRequiredException:
                SetSyncState(SyncState.SignInNeeded);
                break;
            case HttpRequestException or TaskCanceledException:
                SetSyncState(SyncState.Failed, "You're offline or Google Tasks didn't answer");
                break;
            default:
                SetSyncState(SyncState.Failed, ex.Message);
                break;
        }
    }
}
