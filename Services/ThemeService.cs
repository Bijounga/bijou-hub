using System.Windows;

namespace BijouHub.Services;

/// <summary>
/// Swaps the active theme ResourceDictionary at runtime. App.xaml declares the theme
/// dictionary as MergedDictionaries[0] (Styles/Controls.xaml, the shared control templates,
/// always sits at [1]) — Apply relies on that fixed position rather than searching for it.
/// </summary>
public static class ThemeService
{
    public static readonly string[] ThemeNames =
    {
        "Dark", "Light", "Fable", "Fantasy", "HighContrast", "Monochrome",
        "Midnight", "Dos", "Aero", "AeroDark", "Vaporwave", "Y2kChrome"
    };

    // Themes whose readable surface is light (dark ink on a light canvas) — used to pick a
    // matching light/dark native window titlebar (see DarkTitleBar.Apply).
    private static readonly HashSet<string> LightThemes = new()
    {
        "Light", "Fable", "Dos", "Aero", "Vaporwave", "Y2kChrome"
    };

    public static string CurrentThemeName { get; private set; } = "Dark";

    public static bool IsLight(string themeName) => LightThemes.Contains(themeName);

    public static void Apply(string? themeName)
    {
        var name = ThemeNames.Contains(themeName) ? themeName! : "Dark";

        var dict = new ResourceDictionary
        {
            Source = new Uri($"Themes/{name}.xaml", UriKind.Relative)
        };

        Application.Current.Resources.MergedDictionaries[0] = dict;
        CurrentThemeName = name;
    }
}
