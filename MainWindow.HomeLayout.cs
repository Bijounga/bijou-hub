using System.Windows;
using System.Windows.Controls;

namespace BijouHub;

// Home on a narrow, tall window (a monitor turned upright, BijouHub down the left side): today's
// stats move above the task list instead of beside it, and the project cards share the width
// out between them instead of leaving a gap at the right.
public partial class MainWindow
{
    private const double BoardCardGap = 14;   // the card's right margin
    private const double BoardCardLeast = 300;
    private const double BoardCardMost = 440;
    private const double BoardCardMostAlone = 620; // one card per row can be wider
    private const double SideBySideAt = 800;  // wide enough for the stats beside the tasks

    private bool _todayBeside = true;

    private void InitHomeLayout()
    {
        Resources["BoardCardWidth"] = 340.0;
        HomePanel.SizeChanged += (_, _) => FitHome();
    }

    private void FitHome()
    {
        // The panel's own width less its scroll bar (always counted, so a scroll bar appearing
        // can't change the answer and set off another round).
        var width = HomePanel.ActualWidth - 22;
        if (width <= 0) return;

        var beside = width >= SideBySideAt;
        if (beside != _todayBeside)
        {
            _todayBeside = beside;
            if (beside)
            {
                Place(TodayGoalsColumn, row: 0, column: 0, span: 1);
                Place(TodayStatsColumn, row: 0, column: 2, span: 1);
                TodayStatsColumn.Margin = new Thickness(0);
            }
            else
            {
                Place(TodayStatsColumn, row: 0, column: 0, span: 3);
                Place(TodayGoalsColumn, row: 1, column: 0, span: 3);
                TodayStatsColumn.Margin = new Thickness(0, 0, 0, 20);
            }
        }

        var columns = Math.Max(1, (int)Math.Floor(width / (BoardCardLeast + BoardCardGap)));
        var card = Math.Min(Math.Floor(width / columns) - BoardCardGap, columns == 1 ? BoardCardMostAlone : BoardCardMost);
        card = Math.Min(Math.Max(card, 240), Math.Max(240, width - BoardCardGap));
        if (Math.Abs(card - (double)Resources["BoardCardWidth"]) > 0.5) Resources["BoardCardWidth"] = card;
    }

    private static void Place(FrameworkElement element, int row, int column, int span)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, span);
    }
}
