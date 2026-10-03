namespace BijouHub.Models;

public class BlockItem
{
    public string ProcessName { get; set; } = "";

    // Hard-blocked apps get killed the instant they're detected, no prompt, no
    // temporary-allow option — the only way through is ending the session.
    // Soft-blocked (default) apps get the existing alert with Close/Allow 10 min.
    public bool IsHardBlock { get; set; }
}
