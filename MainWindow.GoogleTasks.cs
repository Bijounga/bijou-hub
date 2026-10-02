using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BijouHub.Models;
using BijouHub.Services.GoogleTasks;
using BijouHub.Views;

namespace BijouHub;

// Today's goals synced through Google Tasks (lists named "EDITING - …"), so the same list is on
// a phone (Google Tasks app), the web (tasks.google.com) and other computers. Edits apply on
// screen immediately and are sent to Google in the background, one at a time and in order.
// Pulls happen on opening Home, on coming back to the window, and once a minute.
// Without a Google connection, goals stay local exactly as before.
public partial class MainWindow
{
    private static readonly HttpClient GoogleHttp = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly byte[] TokenEntropy = "BijouHub.GoogleTasks"u8.ToArray();

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
        _googleAuth = new GoogleAuth(GoogleHttp,
            bytes => ProtectedData.Protect(bytes, TokenEntropy, DataProtectionScope.CurrentUser),
            bytes => ProtectedData.Unprotect(bytes, TokenEntropy, DataProtectionScope.CurrentUser));
        _googleSync = new GoogleGoalsSync(new GoogleTasksClient(GoogleHttp, _googleAuth));

        _googlePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _googlePollTimer.Tick += (_, _) =>
        {
            if (IsActive && HomePanel.Visibility == Visibility.Visible) _ = RefreshGoogleGoalsAsync();
        };
        _googlePollTimer.Start();

        // Coming back from the phone or another app: pick up what changed there.
        Activated += (_, _) =>
        {
            if (HomePanel.Visibility == Visibility.Visible && DateTime.Now - _lastGoogleRefresh > TimeSpan.FromSeconds(15))
                _ = RefreshGoogleGoalsAsync();
        };

        SetSyncState(GoogleMode ? SyncState.Syncing : SyncState.Off);
    }

    private void SetSyncState(SyncState state, string? detail = null)
    {
        var (text, brush, tip) = state switch
        {
            SyncState.Syncing => ("Syncing…", "MutedTextBrush", "Talking to Google Tasks"),
            SyncState.Synced => ("✓ Google Tasks", "MutedTextBrush", $"Synced with Google Tasks at {DateTime.Now:t}. Click to manage."),
            SyncState.Failed => ("⚠ Not synced", "HazardBrush", (detail ?? "Couldn't reach Google Tasks") + " — changes will be sent when it's back. Click for details."),
            SyncState.SignInNeeded => ("⚠ Sign in to Google", "HazardBrush", "Google needs you to sign in again. Click to reconnect."),
            _ => ("Sync with Google Tasks", "FaintTextBrush", "Keep these goals in Google Tasks — on your phone, the web and other computers.")
        };
        GoogleSyncText.Text = text;
        GoogleSyncText.SetResourceReference(TextBlock.ForegroundProperty, brush);
        GoogleSyncButton.ToolTip = tip;
    }

    private void GoogleSync_Click(object sender, RoutedEventArgs e)
    {
        if (_googleAuth == null) return;
        var window = new GoogleTasksWindow(_googleAuth) { Owner = this };
        window.ShowDialog();

        if (GoogleMode)
        {
            _ = RefreshGoogleGoalsAsync();
        }
        else
        {
            SetSyncState(SyncState.Off);
            RefreshCarryOver();
            RefreshDailyProjectCombo();
        }
    }

    // Pulls BijouHub's lists and reconciles them into the on-screen list, keeping the user's
    // order. Skipped while something is being edited or sent, so it never fights the user.
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

        if (_googlePending > 0 || _dailyGoals.Any(g => g.IsEditing)) return; // the user moved on mid-fetch; next pull catches up

        LoadDailyPlan(); // past midnight, start the new day's list
        ApplyRemoteGoals(fetched);
        _lastGoogleRefresh = DateTime.Now;
        SetSyncState(SyncState.Synced);
        RefreshCarryOver();
        RefreshDailyProjectCombo();

        // Goals added while offline (or before connecting) go up now.
        foreach (var goal in _dailyGoals.Where(g => g.TaskId == null).ToList())
            QueueGoogleCreate(goal);
    }

    private void ApplyRemoteGoals(List<DailyGoal> fetched)
    {
        var remoteById = fetched.Where(g => g.TaskId != null).ToDictionary(g => g.TaskId!);
        _applyingRemote = true;
        try
        {
            foreach (var goal in _dailyGoals.ToList())
            {
                if (goal.TaskId == null) continue; // not on Google yet

                if (remoteById.Remove(goal.TaskId, out var remote))
                {
                    goal.Text = remote.Text;
                    goal.Starred = remote.Starred;
                    goal.ListId = remote.ListId;
                    goal.ProjectName = remote.ProjectName;
                    goal.ProjectId = remote.ProjectId;
                    goal.CompletedAt = remote.CompletedAt;
                    goal.Done = remote.Done;
                }
                else
                {
                    // Deleted elsewhere, or completed on an earlier day.
                    goal.PropertyChanged -= DailyGoal_PropertyChanged;
                    _dailyGoals.Remove(goal);
                }
            }

            // New from the phone or another computer, in Google's order.
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
        SaveDailyPlan();
    }

    // Runs one change against Google after any already queued. Failures leave the screen as the
    // user set it; the next successful pull reconciles with what Google has.
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
            if (_googlePending == 0) SaveDailyPlan(); // keep new task ids in the local copy
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
                // Deleted while it was being created — remove the copy that just landed on Google.
                if (!_dailyGoals.Contains(goal)) await sync.DeleteAsync(goal);
            }
            finally
            {
                _googleCreating.Remove(goal);
            }
        });
    }

    // Called from DailyGoal_PropertyChanged for edits the user made (not ones pulled from Google).
    private void PushGoalChange(DailyGoal goal, string? property)
    {
        if (!GoogleMode) return;
        switch (property)
        {
            case nameof(DailyGoal.Text):
            case nameof(DailyGoal.Done):
            case nameof(DailyGoal.Starred):
                QueueGoogle(sync => sync.UpdateAsync(goal));
                break;
            case nameof(DailyGoal.ProjectName):
                QueueGoogle(sync => sync.MoveToListAsync(goal));
                break;
        }
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

    // "+ New list…" in the picker: makes "EDITING - <name>" on Google and selects it.
    private async void CreateGoogleListFromPicker()
    {
        var prompt = new TextPromptWindow("New list", "Name for the new list (saved as \"EDITING - name\"):") { Owner = this };
        if (prompt.ShowDialog() != true || string.IsNullOrWhiteSpace(prompt.Value) || _googleSync == null)
        {
            RefreshDailyProjectCombo();
            return;
        }

        var name = prompt.Value.Trim();
        await _googleLock.WaitAsync();
        try
        {
            await _googleSync.CreateListAsync(name);
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
        RefreshDailyProjectCombo(selectListName: name);
    }

    // Lists on Google that aren't BijouHub projects (e.g. "EDITING - Sponsors"), for the pickers.
    private IEnumerable<string> ExtraGoogleListNames() =>
        GoogleMode && _googleSync != null
            ? _googleSync.ListNames.Where(n => !_projects.Any(p => string.Equals(p.Name.Trim(), n, StringComparison.OrdinalIgnoreCase)))
            : Enumerable.Empty<string>();
}
