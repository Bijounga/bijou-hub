using System.IO.Compression;
using System.Text.Json.Nodes;

namespace BijouHub.Services;

// Versions of the BijouHub Stream Deck plugin: the one installed in the Stream Deck app and the
// one in a packed .streamDeckPlugin file (a zip), so the setup window can offer an update when
// a newer plugin brings new keys.
public static class StreamDeckPlugin
{
    public const string Uuid = "com.bijounga.bijouhub";

    // Where the Stream Deck app keeps installed plugins.
    public static string PluginsDir => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "com.elgato.StreamDeck", "Plugins")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins");

    public static bool Installed => Directory.Exists(Path.Combine(PluginsDir, Uuid + ".sdPlugin"));

    // Null when it isn't installed or its manifest can't be read.
    public static Version? InstalledVersion()
    {
        try
        {
            var manifest = Path.Combine(PluginsDir, Uuid + ".sdPlugin", "manifest.json");
            return File.Exists(manifest) ? VersionOf(JsonNode.Parse(File.ReadAllText(manifest))) : null;
        }
        catch
        {
            return null;
        }
    }

    public static Version? PackageVersion(Stream package)
    {
        try
        {
            using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            var entry = zip.GetEntry($"{Uuid}.sdPlugin/manifest.json");
            if (entry == null) return null;
            using var reader = new StreamReader(entry.Open());
            return VersionOf(JsonNode.Parse(reader.ReadToEnd()));
        }
        catch
        {
            return null;
        }
    }

    // True when the package is newer than what's installed (and something is installed).
    public static bool UpdateAvailable(Version? package) =>
        package != null && InstalledVersion() is { } installed && package > installed;

    private static Version? VersionOf(JsonNode? manifest) =>
        Version.TryParse((string?)manifest?["Version"], out var version) ? version : null;
}
