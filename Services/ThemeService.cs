using System.Windows;

namespace BijouHub.Services;

/// <summary>
/// Swaps the active theme at runtime. App.xaml fixes the merge order: [0] theme colors/
/// fonts/radius (swapped here), [1] Styles/Base.xaml (never swapped), [2] the active
/// interactive-control template set (also swapped here, paired per theme below).
/// </summary>
public static class ThemeService
{
    public static readonly string[] ThemeNames =
    {
        "Dark", "Light", "Fable", "Fantasy", "HighContrast", "Monochrome",
        "Midnight", "Dos", "Aero", "AeroDark", "Vaporwave", "Y2kChrome", "NervClassic"
    };

    // Themes whose readable surface is light (dark ink on a light canvas) — used to pick a
    // matching light/dark native window titlebar (see DarkTitleBar.Apply).
    private static readonly HashSet<string> LightThemes = new()
    {
        "Light", "Fable", "Dos", "Aero", "Vaporwave", "Y2kChrome"
    };

    // Which control-template set (Styles/*.xaml) each theme pairs with. Themes not listed
    // here use the default calm set — only NervClassic opts into the glow/bracket one.
    private static readonly Dictionary<string, string> TemplateSets = new()
    {
        ["NervClassic"] = "ControlsNerv"
    };

    public static string CurrentThemeName { get; private set; } = "Dark";

    public static bool IsLight(string themeName) => LightThemes.Contains(themeName);

    // Exposed for MainWindow's pulsing timer glow — an animation, so it can't ride along
    // as a swappable resource the way the rest of the classic chrome does.
    public static bool IsClassicChrome(string themeName) => TemplateSets.ContainsKey(themeName);

    public static void Apply(string? themeName)
    {
        var name = ThemeNames.Contains(themeName) ? themeName! : "Dark";
        var templateSet = TemplateSets.GetValueOrDefault(name, "Controls");

        var themeDict = new ResourceDictionary { Source = new Uri($"Themes/{name}.xaml", UriKind.Relative) };
        var templateDict = new ResourceDictionary { Source = new Uri($"Styles/{templateSet}.xaml", UriKind.Relative) };

        var merged = Application.Current.Resources.MergedDictionaries;
        merged[0] = themeDict;
        merged[2] = templateDict;
        CurrentThemeName = name;
    }
}
