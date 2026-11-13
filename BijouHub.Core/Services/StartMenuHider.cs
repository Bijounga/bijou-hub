using System.Text.Json;

namespace BijouHub.Services;

public enum ShortcutScope { User, Machine }

// A shortcut in the Start menu's Programs folder: what Windows search lists as an app.
public record StartMenuApp(ShortcutScope Scope, string RelativePath)
{
    public string Name => Path.GetFileNameWithoutExtension(RelativePath);
}

// What a hide or restore could not do, so the caller can ask for permission and try again.
public record HideOutcome(List<StartMenuApp> Done, List<StartMenuApp> NeedsAdmin);

// Keeps apps out of Windows search by moving their Start menu shortcut into a folder of our own, and
// moving it back when asked. Windows lists installed apps from those shortcuts, so no shortcut means no
// result (the app itself is untouched and still runs; it just isn't offered when you type its name).
//
// The user's own Start menu can be changed freely. The one shared by every account can't without
// administrator rights: those moves come back as NeedsAdmin and are done through a plan file by an
// elevated copy of BijouHub (see Apply).
public class StartMenuHider
{
    private readonly string _userRoot, _machineRoot, _stashRoot, _listFile;

    public StartMenuHider(string userRoot, string machineRoot, string stashRoot, string listFile)
    {
        _userRoot = userRoot; _machineRoot = machineRoot; _stashRoot = stashRoot; _listFile = listFile;
    }

    private string RootOf(ShortcutScope s) => s == ShortcutScope.User ? _userRoot : _machineRoot;
    private string OriginalPath(StartMenuApp a) => Path.Combine(RootOf(a.Scope), a.RelativePath);
    private string StashPath(StartMenuApp a) => Path.Combine(_stashRoot, a.Scope.ToString(), a.RelativePath);

    // Shortcuts that open something other than the app are left out of the picker.
    private static readonly string[] Noise =
        { "uninstall", "readme", "release notes", "license", "documentation", "help", "manual", "website", "web site", "changelog", "what's new" };

    private static bool IsShortcutFile(string path) =>
        path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    // The relative path must stay inside the Start menu folder: it can come from a plan file.
    private bool Safe(StartMenuApp a)
    {
        if (string.IsNullOrWhiteSpace(a.RelativePath) || Path.IsPathRooted(a.RelativePath) || !IsShortcutFile(a.RelativePath)) return false;
        foreach (var (root, path) in new[] { (RootOf(a.Scope), OriginalPath(a)), (_stashRoot, StashPath(a)) })
        {
            var full = Path.GetFullPath(path);
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    // Every app currently in the Start menu, for the picker.
    public List<StartMenuApp> Visible()
    {
        var apps = new List<StartMenuApp>();
        foreach (var scope in new[] { ShortcutScope.User, ShortcutScope.Machine })
        {
            var root = RootOf(scope);
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (!IsShortcutFile(file)) continue;
                    var rel = Path.GetRelativePath(root, file);
                    var name = Path.GetFileNameWithoutExtension(rel);
                    if (Noise.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
                    apps.Add(new StartMenuApp(scope, rel));
                }
            }
            catch (UnauthorizedAccessException) { /* a folder we can't read */ }
        }
        return apps;
    }

    public List<StartMenuApp> Hidden() => Load();

    private List<StartMenuApp> Load()
    {
        try
        {
            if (!File.Exists(_listFile)) return new();
            var items = JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(_listFile)) ?? new();
            return items.Select(i => new StartMenuApp(i.Scope, i.RelativePath)).ToList();
        }
        catch { return new(); }
    }

    private void Save(List<StartMenuApp> apps) =>
        AtomicFile.WriteAllText(_listFile, JsonSerializer.Serialize(apps.Select(a => new Saved(a.Scope, a.RelativePath)).ToList(), new JsonSerializerOptions { WriteIndented = true }));

    private record Saved(ShortcutScope Scope, string RelativePath);

    private static bool Same(StartMenuApp a, StartMenuApp b) =>
        a.Scope == b.Scope && string.Equals(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase);

    // Moves each shortcut into the stash and remembers it. Ones that can't be moved without
    // administrator rights come back in NeedsAdmin.
    public HideOutcome Hide(IEnumerable<StartMenuApp> apps)
    {
        var saved = Load();
        var done = new List<StartMenuApp>();
        var needsAdmin = new List<StartMenuApp>();
        foreach (var app in apps)
        {
            if (!Safe(app)) continue;
            if (saved.Any(s => Same(s, app))) { done.Add(app); continue; }
            try
            {
                MoveReplacing(OriginalPath(app), StashPath(app));
                saved.Add(app);
                done.Add(app);
            }
            catch (UnauthorizedAccessException) { DropHalfMove(app); needsAdmin.Add(app); }
            catch (IOException) when (app.Scope == ShortcutScope.Machine) { DropHalfMove(app); needsAdmin.Add(app); }
            catch (IOException) { DropHalfMove(app); /* in use or gone: leave it */ }
        }
        Save(saved);
        return new HideOutcome(done, needsAdmin);
    }

    // Puts shortcuts back where they came from.
    public HideOutcome Restore(IEnumerable<StartMenuApp> apps)
    {
        var saved = Load();
        var done = new List<StartMenuApp>();
        var needsAdmin = new List<StartMenuApp>();
        foreach (var app in apps)
        {
            if (!Safe(app)) continue;
            var original = OriginalPath(app);
            var stash = StashPath(app);
            try
            {
                if (File.Exists(stash))
                {
                    // The app made its own copy again meanwhile: keep that one, drop the old.
                    if (File.Exists(original)) File.Delete(stash);
                    else MoveReplacing(stash, original);
                }
                saved.RemoveAll(s => Same(s, app));
                done.Add(app);
            }
            catch (UnauthorizedAccessException) { needsAdmin.Add(app); }
            catch (IOException) when (app.Scope == ShortcutScope.Machine) { needsAdmin.Add(app); }
            catch (IOException) { }
        }
        Save(saved);
        return new HideOutcome(done, needsAdmin);
    }

    // Apps update and reinstall, and often write their shortcut back. Hide those again (the new copy
    // replaces the stashed one, since it may point at the app's new folder). Returns the shared-menu ones
    // that came back, which need administrator rights to hide again.
    public List<StartMenuApp> Recheck()
    {
        var saved = Load();
        var cameBack = new List<StartMenuApp>();
        var changed = false;
        foreach (var app in saved.ToList())
        {
            if (!Safe(app)) continue;
            var original = OriginalPath(app);
            if (File.Exists(original))
            {
                try { MoveReplacing(original, StashPath(app)); }
                catch (UnauthorizedAccessException) { cameBack.Add(app); }
                catch (IOException) { cameBack.Add(app); }
            }
            else if (!File.Exists(StashPath(app)))
            {
                saved.Remove(app); // both gone: nothing left to restore
                changed = true;
            }
        }
        if (changed) Save(saved);
        return cameBack;
    }

    // What an elevated copy of BijouHub runs: only moves, only inside the Start menu and the stash. The
    // normal copy works out what was done afterwards by looking at where the files are (Reconcile).
    public void Apply(IEnumerable<PlanStep> steps)
    {
        foreach (var step in steps)
        {
            var app = new StartMenuApp(step.Scope, step.RelativePath);
            if (!Safe(app)) continue;
            try
            {
                if (step.Hide) MoveReplacing(OriginalPath(app), StashPath(app));
                else if (File.Exists(StashPath(app)))
                {
                    if (File.Exists(OriginalPath(app))) File.Delete(StashPath(app));
                    else MoveReplacing(StashPath(app), OriginalPath(app));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public record PlanStep(bool Hide, ShortcutScope Scope, string RelativePath);

    public static string WritePlan(string file, IEnumerable<PlanStep> steps)
    {
        AtomicFile.WriteAllText(file, JsonSerializer.Serialize(steps.ToList()));
        return file;
    }

    public static List<PlanStep> ReadPlan(string file) =>
        JsonSerializer.Deserialize<List<PlanStep>>(File.ReadAllText(file)) ?? new();

    // After an elevated run: record the shortcuts that really are in the stash, forget the ones that are back.
    public (List<StartMenuApp> Hidden, List<StartMenuApp> Restored, List<StartMenuApp> Failed) Reconcile(IEnumerable<PlanStep> steps)
    {
        var saved = Load();
        var hidden = new List<StartMenuApp>(); var restored = new List<StartMenuApp>(); var failed = new List<StartMenuApp>();
        foreach (var step in steps)
        {
            var app = new StartMenuApp(step.Scope, step.RelativePath);
            if (!Safe(app)) continue;
            var inStash = File.Exists(StashPath(app));
            var inPlace = File.Exists(OriginalPath(app));
            if (step.Hide)
            {
                if (inStash && !inPlace) { if (!saved.Any(s => Same(s, app))) saved.Add(app); hidden.Add(app); }
                else failed.Add(app);
            }
            else
            {
                if (inPlace && !inStash) { saved.RemoveAll(s => Same(s, app)); restored.Add(app); }
                else failed.Add(app);
            }
        }
        Save(saved);
        return (hidden, restored, failed);
    }

    // A move across drives copies first and deletes the original second. If the delete is refused, the
    // copy in the stash must go, or the shortcut would be both hidden and still in the menu.
    private void DropHalfMove(StartMenuApp app)
    {
        try { if (File.Exists(OriginalPath(app)) && File.Exists(StashPath(app))) File.Delete(StashPath(app)); }
        catch { /* leave it */ }
    }

    private static void MoveReplacing(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to, overwrite: true);
    }
}
