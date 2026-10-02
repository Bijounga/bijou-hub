using BijouHub.Models;

namespace BijouHub.Services.GoogleTasks;

// Maps the home page's goals onto Google Tasks. Every list named "EDITING - <name>" belongs to
// BijouHub (other apps keep their own prefixes, e.g. "STUDY - …"): goals linked to a project live
// in "EDITING - <project name>", unlinked ones in "EDITING - General". Lists are created on first
// use. Google's API has no star flag, so a starred goal is a title starting with "★ " — which also
// shows on a phone.
public sealed class GoogleGoalsSync
{
    public const string Prefix = "EDITING - ";
    public const string GeneralList = "General";
    private const string StarMark = "★ ";

    private readonly GoogleTasksClient _client;
    private readonly Dictionary<string, string> _listIds = new(StringComparer.OrdinalIgnoreCase); // name after the prefix → id

    public GoogleGoalsSync(GoogleTasksClient client) => _client = client;

    // Names (after the prefix) of BijouHub's lists as of the last fetch, General excluded.
    public IReadOnlyList<string> ListNames =>
        _listIds.Keys.Where(n => !n.Equals(GeneralList, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToList();

    // Open tasks from every BijouHub list, plus anything completed today, in each list's own order.
    public async Task<List<DailyGoal>> FetchAsync(IEnumerable<(string Id, string Name)> projects, CancellationToken cancel = default)
    {
        await RefreshListsAsync(cancel);
        var projectByName = projects
            .GroupBy(p => Normalize(p.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var fetches = _listIds.Select(async pair =>
        {
            var tasks = await _client.GetTasksAsync(pair.Value, DateTime.Today, cancel);
            return (Name: pair.Key, ListId: pair.Value, Tasks: tasks);
        }).ToList();

        var goals = new List<DailyGoal>();
        foreach (var (name, listId, tasks) in await Task.WhenAll(fetches))
        {
            var isGeneral = name.Equals(GeneralList, StringComparison.OrdinalIgnoreCase);
            projectByName.TryGetValue(Normalize(name), out var project);

            foreach (var task in tasks.OrderBy(t => t.Position, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(task.Title)) continue; // blank rows the phone app leaves behind

                var starred = task.Title.StartsWith('★');
                var goal = new DailyGoal
                {
                    TaskId = task.Id,
                    ListId = listId,
                    Text = starred ? task.Title.TrimStart('★').TrimStart() : task.Title,
                    Starred = starred,
                    ProjectName = isGeneral ? null : (project.Name ?? name),
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
        var listId = await EnsureListAsync(goal.ProjectName ?? GeneralList, cancel);
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

    // Re-linking a goal to another project moves it to that project's list: a copy in the new
    // list, then the old one removed (the API's own move-between-lists isn't relied on).
    public async Task MoveToListAsync(DailyGoal goal, CancellationToken cancel = default)
    {
        if (goal.TaskId == null || goal.ListId == null) return;
        var targetId = await EnsureListAsync(goal.ProjectName ?? GeneralList, cancel);
        if (targetId == goal.ListId) return;

        var created = await _client.CreateTaskAsync(targetId, TitleOf(goal), null, goal.Done, cancel);
        await _client.DeleteTaskAsync(goal.ListId, goal.TaskId, cancel);
        goal.TaskId = created.Id;
        goal.ListId = targetId;
    }

    public async Task CreateListAsync(string name, CancellationToken cancel = default) => await EnsureListAsync(name, cancel);

    private async Task RefreshListsAsync(CancellationToken cancel)
    {
        var lists = await _client.GetTaskListsAsync(cancel);
        _listIds.Clear();
        foreach (var list in lists)
        {
            if (!list.Title.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var name = list.Title[Prefix.Length..].Trim();
            if (name.Length > 0) _listIds.TryAdd(name, list.Id);
        }
    }

    private async Task<string> EnsureListAsync(string name, CancellationToken cancel)
    {
        if (_listIds.TryGetValue(name, out var id)) return id;
        await RefreshListsAsync(cancel); // made on another device since the last fetch?
        if (_listIds.TryGetValue(name, out id)) return id;

        var created = await _client.CreateTaskListAsync(Prefix + name, cancel);
        _listIds[name] = created.Id;
        return created.Id;
    }

    private static string TitleOf(DailyGoal goal) => (goal.Starred ? StarMark : "") + goal.Text;

    private static string Normalize(string? name) =>
        string.Join(' ', (name ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
