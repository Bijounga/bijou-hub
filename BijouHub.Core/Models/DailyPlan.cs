using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BijouHub.Models;

// One day's goals and notes. Kept per day so yesterday's unfinished goals can be offered
// again in the morning.
public class DailyPlan
{
    // Local calendar date, yyyy-MM-dd — a plain string so it never shifts with time zones.
    public string Date { get; set; } = "";
    public List<DailyGoal> Goals { get; set; } = new();
    public string? Notes { get; set; }

    // Set once the morning "carry over yesterday's unfinished goals" offer is used or dismissed.
    public bool CarryOverHandled { get; set; }
}

// Observable so the home checklist updates in place as goals are ticked, starred or linked.
public class DailyGoal : INotifyPropertyChanged
{
    private string _text = "";
    private bool _done;
    private bool _starred;
    private string? _projectId;
    private string? _projectName;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    // Id of the earlier day's goal this was carried over from, if any.
    public string? CarriedFromId { get; set; }

    // Google Tasks ids when goals sync there (null for a goal not yet created on Google).
    public string? TaskId { get; set; }
    public string? ListId { get; set; }

    private string? _group;
    private string? _chipText;

    // List group from the "GROUP - Name" naming convention (EDITING, STUDY, LIFE…; "" for lists
    // without a prefix). Null means EDITING — BijouHub's own goals.
    public string? Group { get => _group; set => Set(ref _group, value); }

    // UI-only: the list label shown on the goal (adds the group on the All tab).
    [JsonIgnore] public string? ChipText { get => _chipText; set => Set(ref _chipText, value); }

    private bool _isEditing;

    // UI-only: the goal's text is open for inline editing.
    [JsonIgnore] public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }

    public string Text { get => _text; set => Set(ref _text, value); }

    public bool Done
    {
        get => _done;
        set
        {
            if (!Set(ref _done, value)) return;
            // Loading from disk sets CompletedAt first — keep it rather than stamping "now".
            if (!value) CompletedAt = null;
            else CompletedAt ??= DateTime.Now;
        }
    }

    // Starred goals are pinned above the rest.
    public bool Starred { get => _starred; set => Set(ref _starred, value); }

    public string? ProjectId { get => _projectId; set => Set(ref _projectId, value); }

    // Copied at link time so the chip still reads right if the project is later deleted.
    public string? ProjectName
    {
        get => _projectName;
        set
        {
            if (Set(ref _projectName, value)) OnPropertyChanged(nameof(HasProject));
        }
    }

    [JsonIgnore] public bool HasProject => !string.IsNullOrEmpty(_projectName);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
