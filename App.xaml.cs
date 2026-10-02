using System.Configuration;
using System.Data;
using System.Windows;
using BijouHub.Services;

namespace BijouHub;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = new AppSettingsStore().Load();
        ThemeService.Apply(settings.ThemeName);

        new MainWindow().Show();
    }
}

