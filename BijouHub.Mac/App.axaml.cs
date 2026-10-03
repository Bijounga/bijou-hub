using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BijouHub.Mac.Services;
using BijouHub.Services;

namespace BijouHub.Mac;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // The saved theme goes on before the window exists, so it never flashes the default.
        MacThemeService.Apply(new AppSettingsStore().Load().ThemeName);

        // A bug in a side window shouldn't take down a running session: log it and carry on.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(DataPaths.LocalDir, "errors.log"), $"{DateTime.Now:s}  {args.Exception}\n\n");
            }
            catch
            {
                // nowhere to write it
            }
            args.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the main window quits, even with the timer popout open (an update relies on it).
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new MainWindow();
            if (desktop.Args?.Contains(MacPlatform.TrayArgument) == true && new AppSettingsStore().Load().KeepRunningWhenClosed)
                window.StartInTray();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
