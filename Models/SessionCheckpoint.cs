namespace BijouHub.Models;

// Written periodically to local disk while a session is active, and read back on the next
// launch to recover time that would otherwise be lost if BijouHub closes ungracefully
// (crash, force-kill, power loss). Deleted whenever a session ends normally.
public class SessionCheckpoint
{
    public DateTime StartTime { get; set; }
    public string ModeName { get; set; } = "";
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? GoalId { get; set; }
    public string? GoalName { get; set; }
    public int ActiveSeconds { get; set; }
    public int IdleSeconds { get; set; }
}
