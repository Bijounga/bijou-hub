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

    private SingleInstance? _instance;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Already running (maybe hidden in the tray)? Ask it to show its window and stop here, so the
        // app icon or a Stream Deck "open app" button brings the window up instead of starting a copy.
        // (A start at sign-in, with --tray, just leaves the running one alone.)
        _instance = SingleInstance.Acquire(DataPaths.LocalDir);
        if (!_instance.IsFirst)
        {
            var quiet = e.Args.Contains(StartupService.TrayArgument);
            if (quiet) { Shutdown(); return; }
            AllowSetForegroundWindow(-1); // let the running one come to the front
            if (_instance.HandOver(TimeSpan.FromSeconds(3)) || !_instance.TakeOver(TimeSpan.FromSeconds(8)))
            {
                Shutdown();
                return;
            }
            // The running one was on its way out and has let go: carry on as the first.
        }

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
        // Started at sign-in with "keep running in the background" on, or with widgets set to open at
        // startup: straight to the tray, with just the widgets showing.
        var widgetsAtStartup = settings.TaskWidget.ShowAtStartup || settings.TimerWidget.ShowAtStartup;
        if (e.Args.Contains(StartupService.TrayArgument) && (settings.KeepRunningWhenClosed || widgetsAtStartup)) window.StartInTray();
        else
        {
            window.Show();
            // Started by something other than the user's own click (a Stream Deck key): Windows would
            // leave the new window behind whatever is in front, with only the always-on-top widgets showing.
            if (!e.Args.Contains(StartupService.TrayArgument)) ForegroundWindow.Raise(window);
        }
        _instance?.Listen(() => Dispatcher.Invoke(window.ShowFromAnotherStart));
        window.OpenStartupWidgets();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Quitting = true;
        base.OnSessionEnding(e);
    }
}

