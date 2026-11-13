using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace BijouHub.Services;

// The Windows side of hiding apps from search: where the Start menu folders are, and running the
// moves that need administrator rights in an elevated copy of BijouHub (one UAC prompt per batch).
public static class SearchHiding
{
    public const string PlanArgument = "--search-plan";

    // BIJOUHUB_STARTMENU_USER / _MACHINE point tests at scratch folders instead of the real Start menu.
    private static string UserRoot => Environment.GetEnvironmentVariable("BIJOUHUB_STARTMENU_USER") is { Length: > 0 } t
        ? t : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");

    private static string MachineRoot => Environment.GetEnvironmentVariable("BIJOUHUB_STARTMENU_MACHINE") is { Length: > 0 } t
        ? t : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");

    public static StartMenuHider Create() => new(
        UserRoot, MachineRoot,
        Path.Combine(DataPaths.LocalDir, "search-hidden"),
        Path.Combine(DataPaths.LocalDir, "search-hidden.json"));

    // Runs in the elevated copy: do the moves in the plan and exit. Only moves, only within the Start
    // menu folders and the stash, and only for the shared (machine) menu.
    public static void RunPlan(string planFile)
    {
        try
        {
            var steps = StartMenuHider.ReadPlan(planFile).Where(s => s.Scope == ShortcutScope.Machine).ToList();
            Create().Apply(steps);
        }
        catch
        {
            // the normal copy checks what actually moved
        }
    }

    // Asks Windows for permission and runs the moves in the shared Start menu. Returns what really
    // happened, or null if the permission prompt was declined.
    public static (List<StartMenuApp> Hidden, List<StartMenuApp> Restored, List<StartMenuApp> Failed)? RunElevated(List<StartMenuHider.PlanStep> steps)
    {
        var exe = Environment.ProcessPath;
        if (exe == null || steps.Count == 0) return null;
        var planFile = Path.Combine(DataPaths.LocalDir, "search-plan.json");
        StartMenuHider.WritePlan(planFile, steps);
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"{PlanArgument} \"{planFile}\"",
                UseShellExecute = true,
                Verb = "runas",
            });
            process?.WaitForExit(30_000);
            return Create().Reconcile(steps);
        }
        catch (Win32Exception)
        {
            return null; // the user said no
        }
        finally
        {
            try { File.Delete(planFile); } catch { /* next run overwrites it */ }
        }
    }
}
