using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// The Stream Deck link — the same protocol as Windows (Core's StreamDeckBridge on 127.0.0.1), so
// one plugin drives either app: Timer and Pomodoro keys, Current Session, Next Goal, Add Time, Pop Out Timer, Daily Target and Quick Capture.
public partial class MainWindow
{
    private StreamDeckBridge? _deckBridge;

    private void InitDeck()
    {
        _deckBridge = new StreamDeckBridge(command => Dispatcher.UIThread.InvokeAsync(command).GetTask(), HandleDeckCommand);
        _deckBridge.Start();
    }

    private DateTime _todayLoggedDay;
    private int _todayLoggedSeconds;

    private int TodayLoggedSeconds()
    {
        if (_todayLoggedDay != DateTime.Today) InvalidateTodayLogged();
        return _todayLoggedSeconds;
    }

    private void InvalidateTodayLogged()
    {
        _boardBuiltAt = DateTime.MinValue;
        _todayLoggedDay = DateTime.Today;
        _todayLoggedSeconds = _logService.GetTodayTotalSeconds();
    }

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
        ["poppedOut"] = _popout != null,
        ["pomodoro"] = DeckPomodoro(),
        ["dailyTargetSeconds"] = _dailyTargetMinutes * 60
    };

    private void BroadcastDeckState() => _deckBridge?.Broadcast(DeckState());

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

    private void BroadcastDeckGoals() => _deckBridge?.Broadcast(DeckGoals());

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
                _ = FinishSessionAsync(askForNote: false); // completes synchronously without the note prompt
                return DeckState();
            case "extend":
                var extendBy = request["minutes"] is JsonValue ev && ev.TryGetValue<int>(out var em) ? em : 15;
                return ExtendSession(extendBy) ? DeckState() : new JsonObject { ["error"] = "No session is running" };
            case "goals":
                return DeckGoals();
            case "completeGoal":
                var goal = _dailyGoals.FirstOrDefault(g => g.Id == (string?)request["goalId"]);
                if (goal == null) return new JsonObject { ["error"] = "That goal is gone" };
                goal.Done = true;
                return DeckGoals();
            case "focus":
                BringToFront();
                return new JsonObject { ["ok"] = true };
            case "capture":
                // After replying, so the plugin isn't left waiting on a window.
                Dispatcher.UIThread.Post(ShowQuickCapture);
                return new JsonObject { ["ok"] = true };
            case "target":
                Dispatcher.UIThread.Post(async () =>
                {
                    BringToFront();
                    await PromptDailyTargetAsync();
                });
                return new JsonObject { ["ok"] = true };
            case "popout":
                if (!IsSessionActive) return new JsonObject { ["error"] = "No session is running" };
                // "show" picks a side; without it the key toggles.
                SetTimerPopout(request["show"] is JsonValue sv && sv.TryGetValue<bool>(out var show) ? show : _popout == null);
                return DeckState();
            default:
                return new JsonObject { ["error"] = "Unknown command" };
        }
    }

    private JsonObject StartFromDeck(JsonObject request)
    {
        var minutes = request["minutes"] is JsonValue m && m.TryGetValue<int>(out var parsed) && parsed > 0 ? parsed : (int?)null;
        // A Pomodoro key sends its break too: focus for `minutes`, break for `breakMinutes`, repeat.
        var rounds = request["rounds"] is JsonValue rv && rv.TryGetValue<int>(out var r) && r > 0 ? r : (int?)null;
        var pomodoro = minutes is int focus && request["breakMinutes"] is JsonValue b && b.TryGetValue<int>(out var rest) && rest > 0
            ? new PomodoroPlan(focus, rest, rounds)
            : null;
        var mode = _modes.FirstOrDefault(x => x.Id == (string?)request["modeId"])
                   ?? _modes.FirstOrDefault(x => string.Equals(x.Name, (string?)request["modeName"], StringComparison.OrdinalIgnoreCase));
        var projectId = (string?)request["projectId"];
        var project = string.IsNullOrEmpty(projectId) ? null : _projects.FirstOrDefault(x => x.Id == projectId);
        if (mode == null && project == null) return new JsonObject { ["error"] = "Mode not found — pick one in the key's settings" };
        if (_sessionStarting) return DeckState();

        _ = StartDeckSessionAsync(mode, project, minutes, (string?)request["keyId"], pomodoro);
        return new JsonObject { ["ok"] = true };
    }

    private async Task StartDeckSessionAsync(WorkMode? mode, Project? project, int? minutes, string? keyId, PomodoroPlan? pomodoro)
    {
        _sessionStarting = true;
        try
        {
            await BeginSession(mode, project, null, minutes, countDown: minutes != null, deckKeyId: keyId, pomodoro: pomodoro);
        }
        finally
        {
            _sessionStarting = false;
        }
    }
}
