using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace BijouHub.Mac.Services;

// The same 17 looks as Windows (Themes/*.axaml is generated from the Windows themes).
// Applying one swaps the app's merged theme dictionary; every style uses DynamicResource, so
// open windows restyle in place.
public static class MacThemeService
{
    // Name, picker label, and the two swatch colors (base, accent) — matching Windows' picker.
    public sealed record ThemeInfo(string Name, string DisplayName, string Base, string Accent);

    public static readonly ThemeInfo[] Themes =
    {
        new("Dark", "Dark", "#030405", "#33E1FF"),
        new("Midnight", "Midnight", "#0C1526", "#4FD1C5"),
        new("Light", "MacBook Light", "#F5F5F7", "#0A64C8"),
        new("HighContrast", "High Contrast", "#000000", "#00E5FF"),
        new("Monochrome", "Monochrome", "#1C1D21", "#E8E8E8"),
        new("Aero", "Frutiger Aero", "#8CC6F2", "#0F9FDB"),
        new("AeroDark", "Frutiger Aero Dark", "#0B2146", "#4CC2FF"),
        new("Aqua", "Frutiger Aqua", "#2BB7CF", "#FF8A6A"),
        new("Dos", "MS-DOS", "#C0C0C0", "#000080"),
        new("Vaporwave", "Vaporwave", "#E8D9FF", "#FF71CE"),
        new("Y2kChrome", "Y2K Chrome", "#D3D8DE", "#F29A17"),
        new("Y2kGunmetal", "Y2K Gunmetal", "#30343A", "#F29A17"),
        new("Studio", "Studio", "#0D1020", "#927EFF"),
        new("DoodleClub", "Doodle Club", "#2459A6", "#FFE45C"),
        new("Fable", "Fable", "#E3CF9C", "#9C2B2B"),
        new("Fantasy", "Earthen", "#2B1A0C", "#D4AF37"),
        new("NervClassic", "NERV Classic", "#030405", "#33E1FF")
    };

    public static string CurrentThemeName { get; private set; } = "Dark";

    public static void Apply(string name)
    {
        if (Application.Current is not { } app) return;
        if (Themes.All(t => t.Name != name)) name = "Dark";

        var include = new ResourceInclude(new Uri("avares://BijouHub.Mac/App.axaml"))
        {
            Source = new Uri($"avares://BijouHub.Mac/Themes/{name}.axaml")
        };
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = include;
        else merged.Add(include);

        app.RequestedThemeVariant = include.Loaded.TryGetResource("ThemeVariantName", null, out var variant) && variant as string == "Light"
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        CurrentThemeName = name;
    }
}
