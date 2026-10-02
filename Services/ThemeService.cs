using System.Windows;

namespace BijouHub.Services;

/// <summary>
/// Swaps the active theme at runtime. App.xaml fixes the merge order: [0] Styles/Base.xaml
/// (never swapped), [1] the theme's control-template set, [2] the theme itself — both
/// swapped here, the theme last so its values win.
/// </summary>
public static class ThemeService
{
    // Order is the order the Theme window lists them in.
    public static readonly string[] ThemeNames =
    {
        "Dark", "Midnight", "Light", "HighContrast", "Monochrome",
        "Aero", "AeroDark", "Aqua", "Dos", "Vaporwave",
        "Y2kChrome", "Y2kGunmetal", "Studio", "DoodleClub", "Fable", "Fantasy", "NervClassic"
    };

    // Themes whose readable surface is light (dark ink on a light canvas) — used to pick a
    // matching light/dark native window titlebar (see DarkTitleBar.Apply).
    private static readonly HashSet<string> LightThemes = new()
    {
        "Light", "Aero", "Aqua", "Dos", "Vaporwave", "Y2kChrome", "DoodleClub", "Fable"
    };

    // Which control-template set (Styles/*.xaml) each theme pairs with; unlisted themes use
    // the calm set.
    private static readonly Dictionary<string, string> TemplateSets = new()
    {
        ["NervClassic"] = "ControlsNerv",
        ["Midnight"] = "ControlsFx",
        ["Light"] = "ControlsFx",
        ["Aero"] = "ControlsFx",
        ["AeroDark"] = "ControlsFx",
        ["Aqua"] = "ControlsFx",
        ["Dos"] = "ControlsFx",
        ["Vaporwave"] = "ControlsFx",
        ["Y2kChrome"] = "ControlsFx",
        ["Y2kGunmetal"] = "ControlsFx",
        ["Studio"] = "ControlsFx",
        ["DoodleClub"] = "ControlsFx",
        ["Fable"] = "ControlsFx",
        ["Fantasy"] = "ControlsFx",
    };

    public static string CurrentThemeName { get; private set; } = "Dark";

    public static bool IsLight(string themeName) => LightThemes.Contains(themeName);

    // Exposed for MainWindow's pulsing timer glow — an animation, so it can't ride along
    // as a swappable resource the way the rest of the classic chrome does.
    public static bool IsClassicChrome(string themeName) =>
        TemplateSets.GetValueOrDefault(themeName) == "ControlsNerv";

    public static void Apply(string? themeName)
    {
        var name = ThemeNames.Contains(themeName) ? themeName! : "Dark";
        var templateSet = TemplateSets.GetValueOrDefault(name, "Controls");

        var templateDict = new ResourceDictionary { Source = new Uri($"Styles/{templateSet}.xaml", UriKind.Relative) };
        var themeDict = new ResourceDictionary { Source = new Uri($"Themes/{name}.xaml", UriKind.Relative) };

        var merged = Application.Current.Resources.MergedDictionaries;
        merged[1] = templateDict;
        merged[2] = themeDict;
        // Base's default brushes derive from theme colors (e.g. the title ink is the theme's
        // TextColor); a brush instance keeps the colors it first resolved, so Base is
        // re-loaded after the theme to build fresh ones against the new colors.
        merged[0] = new ResourceDictionary { Source = new Uri("Styles/Base.xaml", UriKind.Relative) };
        CurrentThemeName = name;
    }
}
