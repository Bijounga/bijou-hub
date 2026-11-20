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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The elevated copy started to move shortcuts in the shared Start menu: do that and leave.
        if (e.Args.Length == 2 && e.Args[0] == SearchHiding.PlanArgument)
        {
            SearchHiding.RunPlan(e.Args[1]);
            Shutdown();
            return;
        }

        // Already running (maybe hidden in the tray)? Ask it to show its window and stop here, so the
        // app icon or a Stream Deck "open app" button brings the window up instead of starting a copy.
        // (A start at sign-in, with --tray, just leaves the running one alone.)
        _instance = SingleInstance.Acquire(DataPaths.LocalDir);
        if (!_instance.IsFirst)
        {
            var retried = e.Args.Contains(StartupWatchdog.RetryArgument);
            var quiet = e.Args.Contains(StartupService.TrayArgument);
            if (quiet && !retried) { Shutdown(); return; }
            AllowSetForegroundWindow(-1); // let the running one come to the front
            if (retried)
            {
                // A stuck copy started this one just before it ended: wait for it to let go.
                if (!_instance.TakeOver(TimeSpan.FromSeconds(15))) { Shutdown(); return; }
            }
            else if (_instance.HandOver(TimeSpan.FromSeconds(3)))
            {
                Shutdown();
                return;
            }
            else if (!_instance.TakeOver(TimeSpan.FromSeconds(8)) && !StuckInstance.Replace(_instance))
            {
                // Still held and not answering, and it couldn't be ended: nothing more this start can do.
                Shutdown();
                return;
            }
            // The running one was on its way out (or was frozen and has been ended) and has let go: carry on as the first.
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

        // If starting up stops moving (it has happened right after signing in), this notes where and starts
        // a fresh copy, instead of leaving a BijouHub with no window that can't be opened again.
        var watchdog = new StartupWatchdog(e.Args);
        watchdog.Start();

        watchdog.Step("reading the settings");
        var settings = new AppSettingsStore().Load();
        watchdog.Step("loading the theme");
        ThemeService.Apply(settings.ThemeName);

        StartupService.UpgradeEntry();

        // Right after signing in, a sync folder on a cloud drive may not be there yet, and asking it from
        // the window's own thread can freeze BijouHub. Ask it from another thread first, and go on once it answers.
        var atSignIn = e.Args.Contains(StartupService.TrayArgument);
        watchdog.Step("waiting for the data folder");
        watchdog.Waiting(true);
        var ready = await DataFolderReady.WaitAsync(settings.DataFolderPath, patient: atSignIn);
        watchdog.Waiting(false);
        if (!ready)
        {
            if (!atSignIn)
                MessageBox.Show($"BijouHub keeps its data in {settings.DataFolderPath}, and that folder isn't answering.\n\nCheck that Google Drive (or whatever syncs it) is running, then open BijouHub again.",
                    "BijouHub", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        watchdog.Step("opening BijouHub");
        var window = new MainWindow();
        // Started at sign-in with "keep running in the background" on, or with widgets set to open at
        // startup: straight to the tray, with just the widgets showing.
        var widgetsAtStartup = settings.TaskWidget.ShowAtStartup || settings.TimerWidget.ShowAtStartup || settings.NotesWidget.ShowAtStartup;
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
        watchdog.Done();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Quitting = true;
        base.OnSessionEnding(e);
    }
}

