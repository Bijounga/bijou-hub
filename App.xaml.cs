using System.IO;
using System.Windows;
using BijouHub.Services;

namespace BijouHub;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // Set when BijouHub should really exit (Quit, an update, Windows signing out), so "keep
    // running when closed" doesn't just minimize it.
    public static bool Quitting { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A bug in a side window (a popup, an alert) shouldn't take down a running session: log
        // it and carry on.
        DispatcherUnhandledException += (_, args) =>
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

        var settings = new AppSettingsStore().Load();
        ThemeService.Apply(settings.ThemeName);

        StartupService.UpgradeEntry();
        var window = new MainWindow();
        // Started at sign-in with "keep running in the background" on: straight to the tray.
        if (e.Args.Contains(StartupService.TrayArgument) && settings.KeepRunningWhenClosed) window.StartInTray();
        else window.Show();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Quitting = true;
        base.OnSessionEnding(e);
    }
}

