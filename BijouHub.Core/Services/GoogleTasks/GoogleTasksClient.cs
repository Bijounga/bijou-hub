using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace BijouHub.Services.GoogleTasks;

// Thin client for the parts of the Google Tasks REST API BijouHub uses: task lists (list,
// create) and tasks (list, create, update, delete). https://developers.google.com/tasks/reference/rest
public sealed class GoogleTasksClient
{
    // BIJOUHUB_GTASKS_BASE points tests at a local fake of the API.
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("BIJOUHUB_GTASKS_BASE") ?? "https://tasks.googleapis.com/tasks/v1").TrimEnd('/');

    private readonly HttpClient _http;
    private readonly GoogleAuth _auth;

    public GoogleTasksClient(HttpClient http, GoogleAuth auth)
    {
        _http = http;
        _auth = auth;
    }

    public sealed record TaskList(string Id, string Title);

    public sealed record TaskItem(string Id, string Title, string? Notes, bool Completed, DateTime? CompletedAt, string? Position);

    public async Task<List<TaskList>> GetTaskListsAsync(CancellationToken cancel = default)
    {
        var lists = new List<TaskList>();
        string? page = null;
        do
        {
            var json = await SendAsync(HttpMethod.Get, $"/users/@me/lists?maxResults=100{PageParam(page)}", null, cancel);
            foreach (var item in json?["items"]?.AsArray() ?? new JsonArray())
                if (item is JsonObject o && (string?)o["id"] is { } id)
                    lists.Add(new TaskList(id, (string?)o["title"] ?? ""));
            page = (string?)json?["nextPageToken"];
        } while (page != null);
        return lists;
    }

    public async Task<TaskList> CreateTaskListAsync(string title, CancellationToken cancel = default)
    {
        var json = await SendAsync(HttpMethod.Post, "/users/@me/lists", new JsonObject { ["title"] = title }, cancel);
        return new TaskList((string)json!["id"]!, (string?)json["title"] ?? title);
    }

    // Open tasks plus those completed since completedSince. Two queries: the API can't express
    // "open OR completed after X" in one, and completed tasks are hidden unless asked for.
    public async Task<List<TaskItem>> GetTasksAsync(string listId, DateTime completedSince, CancellationToken cancel = default)
    {
        var open = await QueryTasksAsync(listId, "showCompleted=false", cancel);
        var since = Uri.EscapeDataString(completedSince.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var done = await QueryTasksAsync(listId, $"showCompleted=true&showHidden=true&completedMin={since}", cancel);
        return open.Concat(done.Where(t => t.Completed)).GroupBy(t => t.Id).Select(g => g.First()).ToList();
    }

    public async Task<TaskItem> CreateTaskAsync(string listId, string title, string? notes, bool completed, CancellationToken cancel = default)
    {
        var body = new JsonObject { ["title"] = title, ["status"] = completed ? "completed" : "needsAction" };
        if (!string.IsNullOrEmpty(notes)) body["notes"] = notes;
        var json = await SendAsync(HttpMethod.Post, $"/lists/{Esc(listId)}/tasks", body, cancel);
        return Parse((JsonObject)json!);
    }

    public async Task UpdateTaskAsync(string listId, string taskId, string? title = null, bool? completed = null, CancellationToken cancel = default)
    {
        var body = new JsonObject();
        if (title != null) body["title"] = title;
        if (completed is bool isDone)
        {
            body["status"] = isDone ? "completed" : "needsAction";
            if (!isDone) body["completed"] = null; // reopening must clear the completion time too
        }
        await SendAsync(HttpMethod.Patch, $"/lists/{Esc(listId)}/tasks/{Esc(taskId)}", body, cancel);
    }

    public Task DeleteTaskAsync(string listId, string taskId, CancellationToken cancel = default) =>
        SendAsync(HttpMethod.Delete, $"/lists/{Esc(listId)}/tasks/{Esc(taskId)}", null, cancel);

    private async Task<List<TaskItem>> QueryTasksAsync(string listId, string filter, CancellationToken cancel)
    {
        var tasks = new List<TaskItem>();
        string? page = null;
        do
        {
            var json = await SendAsync(HttpMethod.Get, $"/lists/{Esc(listId)}/tasks?maxResults=100&{filter}{PageParam(page)}", null, cancel);
            foreach (var item in json?["items"]?.AsArray() ?? new JsonArray())
                if (item is JsonObject o && o["deleted"]?.GetValue<bool>() != true)
                    tasks.Add(Parse(o));
            page = (string?)json?["nextPageToken"];
        } while (page != null);
        return tasks;
    }

    private static TaskItem Parse(JsonObject o)
    {
        DateTime? completedAt = DateTime.TryParse((string?)o["completed"], CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var c) ? c.ToLocalTime() : null;
        return new TaskItem(
            (string)o["id"]!,
            (string?)o["title"] ?? "",
            (string?)o["notes"],
            (string?)o["status"] == "completed",
            completedAt,
            (string?)o["position"]);
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancel)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _auth.GetAccessTokenAsync(cancel));
            if (body != null)
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            HttpResponseMessage sent;
            try
            {
                sent = await _http.SendAsync(request, cancel);
            }
            catch (HttpRequestException) when (method == HttpMethod.Get && attempt == 0 && !cancel.IsCancellationRequested)
            {
                continue; // a pooled connection the far end had already dropped — reads are safe to repeat
            }

            using var response = sent;
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _auth.InvalidateAccessToken(); // expired early or revoked — refresh once and retry
                continue;
            }

            var text = await response.Content.ReadAsStringAsync(cancel);
            if (!response.IsSuccessStatusCode)
            {
                string? message = null;
                try { message = (string?)JsonNode.Parse(text)?["error"]?["message"]; }
                catch { /* not the usual {error:{message}} shape */ }
                throw new GoogleTasksException(response.StatusCode, message ?? response.ReasonPhrase ?? "Request failed");
            }
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
    }

    private static string Esc(string value) => Uri.EscapeDataString(value);

    private static string PageParam(string? page) => page == null ? "" : $"&pageToken={Uri.EscapeDataString(page)}";
}

public sealed class GoogleTasksException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
