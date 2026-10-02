using System.Text.RegularExpressions;
using BijouHub.Models;

namespace BijouHub.Services.GoogleTasks;

// Maps the home page's goals onto Google Tasks. Lists follow a "GROUP - Name" convention shared
// with the user's other apps ("EDITING - Allumeria", "STUDY - CSCI 3740", "LIFE - Goals"); each
// group becomes a tab on the home page, and lists without a prefix (Google's default "My Tasks")
// form the "" group. BijouHub's own goals live in the EDITING group: goals linked to a project in
// "EDITING - <project name>", unlinked ones in "EDITING - General". Lists are created on first use.
// Google's API has no star flag, so a starred goal is a title starting with "★ " — which also
// shows on a phone.
public sealed partial class GoogleGoalsSync
{
    public const string EditingGroup = "EDITING";
    public const string GeneralList = "General";
    private const string StarMark = "★ ";

    private readonly GoogleTasksClient _client;
    private readonly List<ListInfo> _lists = new();

    public GoogleGoalsSync(GoogleTasksClient client) => _client = client;

    private sealed record ListInfo(string Id, string Title, string Group, string Name);

    // "STUDY - CSCI 3740" → ("STUDY", "CSCI 3740"); "My Tasks" → ("", "My Tasks").
    public static (string Group, string Name) ParseTitle(string title)
    {
        var match = PrefixPattern().Match(title);
        return match.Success
            ? (match.Groups[1].Value.Trim().ToUpperInvariant(), match.Groups[2].Value.Trim())
            : ("", title.Trim());
    }

    // The list a goal belongs in. A null name means the group's General list.
    public static string TitleFor(string group, string? name) =>
        group.Length == 0 ? name ?? "My Tasks" : $"{group} - {name ?? GeneralList}";

    public static string GroupOf(DailyGoal goal) => goal.Group ?? EditingGroup;

    // Groups seen at the last fetch; EDITING is always there (it's created on first use).
    public IReadOnlyList<string> Groups =>
        _lists.Select(l => l.Group).Append(EditingGroup).Distinct().ToList();

    // List names inside a group, without its General list (that's the "no project" choice).
    public IReadOnlyList<string> ListNames(string group) =>
        _lists.Where(l => l.Group == group && !(group.Length > 0 && l.Name.Equals(GeneralList, StringComparison.OrdinalIgnoreCase)))
            .Select(l => l.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // Open tasks from every list, plus anything completed today, in each list's own order.
    public async Task<List<DailyGoal>> FetchAsync(IEnumerable<(string Id, string Name)> projects, CancellationToken cancel = default)
    {
        await RefreshListsAsync(cancel);
        var projectByName = projects
            .GroupBy(p => Normalize(p.Name))
            .ToDictionary(g => g.Key, g => g.First());

        // A few lists at a time: quick, without bursting a request per list at Google.
        using var gate = new SemaphoreSlim(4);
        var fetches = _lists.Select(async list =>
        {
            await gate.WaitAsync(cancel);
            try { return (List: list, Tasks: await _client.GetTasksAsync(list.Id, DateTime.Today, cancel)); }
            finally { gate.Release(); }
        }).ToList();

        var goals = new List<DailyGoal>();
        foreach (var (list, tasks) in await Task.WhenAll(fetches))
        {
            var isGeneral = list.Group.Length > 0 && list.Name.Equals(GeneralList, StringComparison.OrdinalIgnoreCase);
            (string Id, string Name) project = default;
            if (list.Group == EditingGroup) projectByName.TryGetValue(Normalize(list.Name), out project);

            foreach (var task in tasks.OrderBy(t => t.Position, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(task.Title)) continue; // blank rows the phone app leaves behind

                var starred = task.Title.StartsWith('★');
                var goal = new DailyGoal
                {
                    TaskId = task.Id,
                    ListId = list.Id,
                    Group = list.Group,
                    Text = starred ? task.Title.TrimStart('★').TrimStart() : task.Title,
                    Starred = starred,
                    ProjectName = isGeneral ? null : (project.Name ?? list.Name),
                    ProjectId = isGeneral ? null : project.Id,
                    CompletedAt = task.CompletedAt
                };
                goal.Done = task.Completed; // after CompletedAt, so the real completion time is kept
                goals.Add(goal);
            }
        }
        return goals;
    }

    public async Task CreateAsync(DailyGoal goal, CancellationToken cancel = default)
    {
        var listId = await EnsureListAsync(TitleFor(GroupOf(goal), goal.ProjectName), cancel);
        var created = await _client.CreateTaskAsync(listId, TitleOf(goal), null, goal.Done, cancel);
        goal.TaskId = created.Id;
        goal.ListId = listId;
    }

    public Task UpdateAsync(DailyGoal goal, CancellationToken cancel = default) =>
        goal.TaskId == null || goal.ListId == null
            ? Task.CompletedTask
            : _client.UpdateTaskAsync(goal.ListId, goal.TaskId, TitleOf(goal), goal.Done, cancel);

    public Task DeleteAsync(DailyGoal goal, CancellationToken cancel = default) =>
        goal.TaskId == null || goal.ListId == null
            ? Task.CompletedTask
            : _client.DeleteTaskAsync(goal.ListId, goal.TaskId, cancel);

    // Moving a goal to another list: a copy in the new list, then the old one removed (the API's
    // own move-between-lists isn't relied on).
    public async Task MoveToListAsync(DailyGoal goal, CancellationToken cancel = default)
    {
        if (goal.TaskId == null || goal.ListId == null) return;
        var targetId = await EnsureListAsync(TitleFor(GroupOf(goal), goal.ProjectName), cancel);
        if (targetId == goal.ListId) return;

        var created = await _client.CreateTaskAsync(targetId, TitleOf(goal), null, goal.Done, cancel);
        await _client.DeleteTaskAsync(goal.ListId, goal.TaskId, cancel);
        goal.TaskId = created.Id;
        goal.ListId = targetId;
    }

    public async Task CreateListAsync(string title, CancellationToken cancel = default) => await EnsureListAsync(title, cancel);

    private async Task RefreshListsAsync(CancellationToken cancel)
    {
        var lists = await _client.GetTaskListsAsync(cancel);
        _lists.Clear();
        foreach (var list in lists)
        {
            var (group, name) = ParseTitle(list.Title);
            if (name.Length > 0) _lists.Add(new ListInfo(list.Id, list.Title, group, name));
        }
    }

    private ListInfo? Find(string title)
    {
        var (group, name) = ParseTitle(title);
        return _lists.FirstOrDefault(l => l.Group == group && l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> EnsureListAsync(string title, CancellationToken cancel)
    {
        if (Find(title) is { } known) return known.Id;
        await RefreshListsAsync(cancel); // made on another device since the last fetch?
        if (Find(title) is { } fresh) return fresh.Id;

        var created = await _client.CreateTaskListAsync(title, cancel);
        var (group, name) = ParseTitle(created.Title);
        _lists.Add(new ListInfo(created.Id, created.Title, group, name));
        return created.Id;
    }

    private static string TitleOf(DailyGoal goal) => (goal.Starred ? StarMark : "") + goal.Text;

    private static string Normalize(string? name) =>
        string.Join(' ', (name ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // A short word-ish prefix, then " - ", then the name. Keeps a title like "Q3 - review" grouped
    // while leaving ordinary titles with a dash in the middle of a sentence alone.
    [GeneratedRegex(@"^\s*([\p{L}\p{N}][\p{L}\p{N} &]{0,23}?)\s+-\s+(.+?)\s*$")]
    private static partial Regex PrefixPattern();
}
