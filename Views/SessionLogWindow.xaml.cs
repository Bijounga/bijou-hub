using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BijouHub.Services;

namespace BijouHub.Views;

public partial class SessionLogWindow : Window
{
    public SessionLogWindow(SessionLogService logService)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        var sessions = logService.GetAll();
        var byDay = sessions.GroupBy(s => s.StartTime.Date).OrderByDescending(g => g.Key);

        foreach (var day in byDay)
        {
            var dayPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };

            var totalActive = TimeSpan.FromSeconds(day.Sum(s => s.ActiveSeconds));
            dayPanel.Children.Add(new TextBlock
            {
                Text = $"{day.Key:dddd, MMM d}   —   {FormatSpan(totalActive)} total",
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                Margin = new Thickness(0, 0, 0, 6)
            });

            var byMode = day.GroupBy(s => s.ModeName)
                .Select(g => $"{g.Key}: {FormatSpan(TimeSpan.FromSeconds(g.Sum(s => s.ActiveSeconds)))}");
            dayPanel.Children.Add(new TextBlock
            {
                Text = string.Join("   •   ", byMode),
                Foreground = (Brush)Application.Current.Resources["MutedTextBrush"],
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var s in day.OrderByDescending(s => s.StartTime))
            {
                var idleNote = s.IdleSeconds > 0 ? $"  (idle {FormatSpan(TimeSpan.FromSeconds(s.IdleSeconds))})" : "";
                dayPanel.Children.Add(new TextBlock
                {
                    Text = $"  {s.StartTime:HH:mm} - {s.EndTime:HH:mm}   {s.ModeName}   {FormatSpan(TimeSpan.FromSeconds(s.ActiveSeconds))}{idleNote}",
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }

            LogItems.Items.Add(dayPanel);
        }

        if (!sessions.Any())
        {
            LogItems.Items.Add(new TextBlock
            {
                Text = "No sessions logged yet. Finish a session to see it here.",
                Foreground = (Brush)Application.Current.Resources["MutedTextBrush"]
            });
        }
    }

    private static string FormatSpan(TimeSpan span)
    {
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m {span.Seconds}s";
    }
}
