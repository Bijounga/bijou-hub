using System.Windows;
using System.Windows.Threading;

namespace BijouHub;

// Time logged from another computer (the history is a shared file) reaches Home and the Stream Deck
// without waiting for a session here to end. Re-reading is cheap: the log is only parsed again when
// its file has changed.
public partial class MainWindow
{
    private DispatcherTimer? _logWatch;

    private void InitLogWatch()
    {
        _logWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _logWatch.Tick += (_, _) => RefreshLoggedTime();
        _logWatch.Start();
    }

    private void RefreshLoggedTime()
    {
        if (_todayLoggedDay == DateTime.Today && _logService.GetTodayTotalSeconds() == _todayLoggedSeconds) return;

        InvalidateTodayLogged();
        if (HomePanel.Visibility == Visibility.Visible)
        {
            UpdateTodayCard();
            BuildWeekBars(_logService.GetLastNDaysTotals(7));
        }
        BroadcastDeckState();
    }
}
