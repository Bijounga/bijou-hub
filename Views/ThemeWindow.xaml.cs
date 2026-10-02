using System.Windows;
using System.Windows.Media;
using BijouHub.Services;

namespace BijouHub.Views;

public partial class ThemeWindow : Window
{
    private record ThemeOption(string Name, string DisplayName, Brush Base, Brush Accent);

    // Display names match BijouDocs / BijouMusic / Bijou Footage. Swatch colors are a
    // hand-picked summary of each look (its base and its accent), not values read out of
    // the theme files — loading every theme just to draw its swatch would be wasteful.
    private static readonly (string Name, string DisplayName, string Base, string Accent)[] Catalog =
    {
        ("Dark", "Dark", "#030405", "#33E1FF"),
        ("Midnight", "Midnight", "#0C1526", "#4FD1C5"),
        ("Light", "MacBook Light", "#F5F5F7", "#0A64C8"),
        ("HighContrast", "High Contrast", "#000000", "#00E5FF"),
        ("Monochrome", "Monochrome", "#1C1D21", "#E8E8E8"),
        ("Aero", "Frutiger Aero", "#8CC6F2", "#0F9FDB"),
        ("AeroDark", "Frutiger Aero Dark", "#0B2146", "#4CC2FF"),
        ("Aqua", "Frutiger Aqua", "#2BB7CF", "#FF8A6A"),
        ("Dos", "MS-DOS", "#C0C0C0", "#000080"),
        ("Vaporwave", "Vaporwave", "#E8D9FF", "#FF71CE"),
        ("Y2kChrome", "Y2K Chrome", "#D3D8DE", "#F29A17"),
        ("Y2kGunmetal", "Y2K Gunmetal", "#30343A", "#F29A17"),
        ("Studio", "Studio", "#0D1020", "#927EFF"),
        ("DoodleClub", "Doodle Club", "#2459A6", "#FFE45C"),
        ("Fable", "Fable", "#E3CF9C", "#9C2B2B"),
        ("Fantasy", "Earthen", "#2B1A0C", "#D4AF37"),
        ("NervClassic", "NERV Classic", "#030405", "#33E1FF"),
    };

    private static Brush Hex(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private readonly AppSettingsStore _settingsStore;
    private bool _isLoading = true;

    public ThemeWindow(AppSettingsStore settingsStore)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _settingsStore = settingsStore;

        var options = Catalog.Select(t => new ThemeOption(t.Name, t.DisplayName, Hex(t.Base), Hex(t.Accent))).ToList();

        ThemesList.ItemsSource = options;
        ThemesList.SelectedItem = options.FirstOrDefault(o => o.Name == ThemeService.CurrentThemeName)
                                   ?? options[0];
        ThemesList.ScrollIntoView(ThemesList.SelectedItem);
        _isLoading = false;
    }

    private void ThemesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (ThemesList.SelectedItem is not ThemeOption option) return;

        ThemeService.Apply(option.Name);
        DarkTitleBar.Apply(this);
        if (Owner != null) DarkTitleBar.Apply(Owner);
        if (Owner is MainWindow mainWindow) mainWindow.RefreshThemeChrome();

        var settings = _settingsStore.Load();
        settings.ThemeName = option.Name;
        _settingsStore.Save(settings);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
