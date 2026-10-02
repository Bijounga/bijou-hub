using System.Windows;
using System.Windows.Media;
using BijouHub.Services;

namespace BijouHub.Views;

public partial class ThemeWindow : Window
{
    private record ThemeOption(string Name, string DisplayName, Brush Swatch);

    // Swatches are the same accent hex each theme file defines — kept here as a small,
    // duplicated lookup purely so the picker can show a preview color without loading
    // every theme's ResourceDictionary just to read one value back out of it.
    private static readonly (string Name, string DisplayName, string Accent)[] Catalog =
    {
        ("Dark", "Dark", "#33E1FF"),
        ("Light", "Light", "#005BB8"),
        ("Fable", "Fable", "#9C2B2B"),
        ("Fantasy", "Fantasy", "#D4AF37"),
        ("HighContrast", "High Contrast", "#00E5FF"),
        ("Monochrome", "Monochrome", "#E8E8E8"),
        ("Midnight", "Midnight", "#4FD1C5"),
        ("Dos", "DOS", "#000080"),
        ("Aero", "Aero", "#0F9FDB"),
        ("AeroDark", "Aero Dark", "#4CC2FF"),
        ("Vaporwave", "Vaporwave", "#E0359B"),
        ("Y2kChrome", "Y2K Chrome", "#A85206"),
        ("NervClassic", "NERV Classic", "#33E1FF"),
    };

    private readonly AppSettingsStore _settingsStore;
    private bool _isLoading = true;

    public ThemeWindow(AppSettingsStore settingsStore)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _settingsStore = settingsStore;

        var options = Catalog.Select(t => new ThemeOption(t.Name, t.DisplayName,
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(t.Accent)))).ToList();

        ThemesList.ItemsSource = options;
        ThemesList.SelectedItem = options.FirstOrDefault(o => o.Name == ThemeService.CurrentThemeName)
                                   ?? options[0];
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
