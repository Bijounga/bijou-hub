namespace BijouHub.Models;

public class SessionRecord
{
    public int Id { get; set; }
    public string ModeName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int ActiveSeconds { get; set; }
    public int IdleSeconds { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? GoalId { get; set; }
    public string? GoalName { get; set; }

    // The task (from the task list) being worked on when the session ended, if one was picked.
    public string? TaskId { get; set; }
    public string? TaskName { get; set; }
    public string? Note { get; set; }
}
