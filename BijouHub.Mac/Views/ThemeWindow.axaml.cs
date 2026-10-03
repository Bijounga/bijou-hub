using Avalonia.Controls;
using Avalonia.Interactivity;
using BijouHub.Mac.Services;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

// Picking a theme applies it live and saves it.
public partial class ThemeWindow : Window
{
    private readonly AppSettingsStore _settings;

    public ThemeWindow() : this(new AppSettingsStore()) { }

    public ThemeWindow(AppSettingsStore settings)
    {
        InitializeComponent();
        _settings = settings;
        ThemesList.ItemsSource = MacThemeService.Themes;
        ThemesList.SelectedItem = MacThemeService.Themes.FirstOrDefault(t => t.Name == MacThemeService.CurrentThemeName);
        Opened += (_, _) => { if (ThemesList.SelectedItem != null) ThemesList.ScrollIntoView(ThemesList.SelectedItem); };
    }

    private void ThemesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemesList.SelectedItem is not MacThemeService.ThemeInfo theme || theme.Name == MacThemeService.CurrentThemeName) return;
        MacThemeService.Apply(theme.Name);
        var settings = _settings.Load();
        settings.ThemeName = theme.Name;
        _settings.Save(settings);
    }

    private void Done_Click(object? sender, RoutedEventArgs e) => Close();
}
