using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BijouHub.Mac.Controls;
using BijouHub.Mac.Services;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;
using Path = System.IO.Path;

namespace BijouHub.Mac;

// Home: the date, today's goals (MainWindow.Goals.cs), time worked, the project board (formerly
// BijouBoard) and quick-launch apps.
public partial class MainWindow
{
    private void ShowHome()
    {
        ShowOnly(HomePanel);
        _detailProject = null;
        HomeDateText.Text = DateTime.Today.ToString("dddd, MMMM d");
        HomeGreetingText.Text = Greetings.Random();

        // Fresh from the log (another computer may have added time), then kept live by the tick.
        _todayLoggedDay = DateTime.Today;
        _todayLoggedSeconds = _logService.GetTodayTotalSeconds();
        UpdateTodayCard();
        BuildWeekBars(_logService.GetLastNDaysTotals(7));

        LoadDailyPlan();
        RefreshDailyProjectCombo();
        _ = RefreshBoardAsync();
        _ = RefreshGoogleGoalsAsync();
        RefreshQuickLaunch();
    }

    private static string FormatSpan(int totalSeconds)
    {
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m";
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private void BuildWeekBars(Dictionary<DateTime, int> days)
    {
        const double area = 56;
        var max = Math.Max(1, days.Values.DefaultIfEmpty(0).Max());
        HomeWeekBars.Children.Clear();
        foreach (var (day, seconds) in days.OrderBy(kv => kv.Key))
        {
            var isToday = day == DateTime.Today;
            var bar = new Border
            {
                Width = 14,
                Height = seconds == 0 ? 3 : Math.Max(4, area * seconds / max),
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Bottom,
                Opacity = seconds == 0 ? 0.25 : isToday ? 1 : 0.5,
                Background = Brush("AccentBrush")
            };
            var label = new TextBlock
            {
                Text = day.ToString("ddd")[..1],
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                FontWeight = isToday ? FontWeight.Bold : FontWeight.Normal,
                Foreground = Brush(isToday ? "TextBrush" : "MutedTextBrush")
            };
            var column = new StackPanel();
            ToolTip.SetTip(column, $"{day:dddd}: {FormatSpan(seconds)}");
            column.Children.Add(new Panel { Height = area, Children = { bar } });
            column.Children.Add(label);
            HomeWeekBars.Children.Add(column);
        }
        HomeWeekText.Text = $"Last 7 days: {FormatSpan(days.Values.Sum())}";
    }

    // ---------- Project board ----------

    private ProjectBoardService.Board? _board;
    private DateTime _boardBuiltAt;

    private async Task RefreshBoardAsync(bool force = false)
    {
        if (!force && _board != null && DateTime.Now - _boardBuiltAt < TimeSpan.FromSeconds(30))
        {
            ShowBoard();
            return;
        }
        _boardBuiltAt = DateTime.Now;
        var projects = _projects.ToList();
        var sessions = _logService.GetAll();
        _board = await Task.Run(() => ProjectBoardService.Build(projects, sessions));
        ShowBoard();
    }

    private async void RefreshBoard_Click(object? sender, RoutedEventArgs e) => await RefreshBoardAsync(force: true);

    private void ShowBoard()
    {
        if (_board == null) return;
        BoardSummary.Children.Clear();
        BoardSummary.Children.Add(SummaryPill(_board.ReadyToPublish, "ready to publish", "SuccessBrush"));
        BoardSummary.Children.Add(SummaryPill(_board.BehindPace, "behind pace", "DangerBrush"));
        BoardSummary.Children.Add(SummaryPill(_board.NeedMusic, "need music", "HazardBrush"));
        BoardSummary.Children.Add(SummaryPill(_board.Active, _board.Active == 1 ? "active project" : "active projects", "AccentBrush"));
        BoardCards.ItemsSource = _board.Cards.Select(BuildCard).ToList();
        BoardEmptyText.IsVisible = _board.Cards.Count == 0;
    }

    private static string ToneKey(string tone) => tone switch
    {
        "success" => "SuccessBrush",
        "warning" => "HazardBrush",
        "critical" => "DangerBrush",
        "accent" => "AccentBrush",
        _ => "MutedTextBrush"
    };

    private Border SummaryPill(int count, string label, string tone) => new()
    {
        CornerRadius = new CornerRadius(14),
        BorderThickness = new Thickness(1),
        BorderBrush = Brush("BorderBrush"),
        Background = Brush("CardBrush"),
        Padding = new Thickness(12, 5, 14, 5),
        Margin = new Thickness(0, 0, 8, 8),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children =
            {
                new Ellipse { Width = 7, Height = 7, Fill = Brush(tone), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = count.ToString(), FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = label, FontSize = 12, Foreground = Brush("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center }
            }
        }
    };

    private Border BuildCard(ProjectBoardService.Card card)
    {
        var toneBrush = (IBrush)Brush(ToneKey(card.StatusTone));
        var softTone = toneBrush is ISolidColorBrush solid
            ? new SolidColorBrush(Color.FromArgb(0x2E, solid.Color.R, solid.Color.G, solid.Color.B))
            : Brush("CardHoverBrush");

        Grid Row(string label, string tone, string value, string detail)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("62,*"), Margin = new Thickness(0, 0, 0, 7) };
            grid.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = Brush("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center });
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, [Grid.ColumnProperty] = 1 };
            if (tone.Length > 0) line.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Brush(ToneKey(tone)), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(new TextBlock { Text = value, FontSize = 13 });
            if (detail.Length > 0)
                line.Children.Add(new TextBlock { Text = $"— {detail}", FontSize = 12, Foreground = Brush("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 });
            grid.Children.Add(line);
            return grid;
        }

        var progress = new Panel { Height = 6, Margin = new Thickness(0, 0, 0, 0) };
        progress.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Brush("SoftBorderBrush") });
        progress.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = Brush("AccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(0, 306 * card.GoalsFraction)
        });

        var body = new StackPanel();
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = card.Title, FontSize = 16, FontWeight = FontWeight.Bold, MaxWidth = 190, TextTrimming = TextTrimming.CharacterEllipsis },
                new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2), Background = softTone, VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = card.StatusPillText, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = toneBrush }
                }
            }
        });
        body.Children.Add(new TextBlock { Text = card.Subtitle, FontSize = 11, Foreground = Brush("MutedTextBrush"), Margin = new Thickness(0, 3, 0, 14) });
        body.Children.Add(Row("SCRIPT", card.ScriptTone, card.ScriptLabel, card.ScriptDetail));
        body.Children.Add(Row("MUSIC", card.MusicTone, card.MusicLabel, card.MusicDetail));
        body.Children.Add(Row("TIME", "", card.TimeThisWeek, "this week"));
        body.Children.Add(new TextBlock { Text = card.GoalsText, FontSize = 12, Margin = new Thickness(0, 7, 0, 6) });
        body.Children.Add(progress);
        if (card.NextGoal.Length > 0)
            body.Children.Add(new TextBlock { Text = card.NextGoal, FontSize = 11, Foreground = Brush("MutedTextBrush"), Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

        var border = new Border
        {
            Width = 340,
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(16, 14),
            CornerRadius = (CornerRadius)(this.TryFindResource("ControlCornerRadius", out var r) && r is CornerRadius cr ? cr : new CornerRadius(8)),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = body,
            Classes = { "card", "boardcard" }
        };
        border.PointerReleased += (_, _) =>
        {
            if (card.Project == _activeProject && IsSessionActive) ShowSession();
            else SelectProject(card.Project);
        };
        return border;
    }

    // ---------- Quick launch (Mac apps, files or folders you open often) ----------

    private sealed class QuickApp
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
    }

    private static string QuickLaunchPath => Path.Combine(DataPaths.LocalDir, "quicklaunch.json");

    private List<QuickApp> LoadQuickApps()
    {
        try
        {
            return File.Exists(QuickLaunchPath)
                ? JsonSerializer.Deserialize<List<QuickApp>>(File.ReadAllText(QuickLaunchPath)) ?? new List<QuickApp>()
                : new List<QuickApp>();
        }
        catch
        {
            return new List<QuickApp>();
        }
    }

    private void SaveQuickApps(List<QuickApp> apps) =>
        AtomicFile.WriteAllText(QuickLaunchPath, JsonSerializer.Serialize(apps, new JsonSerializerOptions { WriteIndented = true }));

    private void RefreshQuickLaunch()
    {
        QuickLaunchPanel.Children.Clear();
        var apps = LoadQuickApps();
        foreach (var app in apps) QuickLaunchPanel.Children.Add(QuickTile(app, apps));
        QuickLaunchPanel.Children.Add(AddQuickTile());
    }

    private Button TileButton(Control icon, string label)
    {
        var button = new Button
        {
            Width = 96,
            Height = 92,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(6),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    icon,
                    new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 84 }
                }
            }
        };
        ToolTip.SetTip(button, label);
        return button;
    }

    private Button QuickTile(QuickApp app, List<QuickApp> apps)
    {
        var button = TileButton(new LineIcon { Kind = "app", Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center }, app.Name);
        button.Click += (_, _) =>
        {
            try { MacPlatform.Open(app.Path); }
            catch (Exception ex) { _ = PromptWindow.Notice(this, "Couldn't open it", ex.Message); }
        };
        var remove = new MenuItem { Header = "Remove" };
        remove.Click += (_, _) =>
        {
            apps.RemoveAll(a => a.Id == app.Id);
            SaveQuickApps(apps);
            RefreshQuickLaunch();
        };
        button.ContextMenu = new ContextMenu { Items = { remove } };
        return button;
    }

    private Button AddQuickTile()
    {
        var button = TileButton(new LineIcon { Kind = "add", Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center }, "Add app");
        button.Opacity = 0.7;
        button.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose an app (or any file) to keep on Home",
                AllowMultiple = false,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(OperatingSystem.IsMacOS() ? "/Applications" : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
            });
            var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (string.IsNullOrEmpty(path)) return;
            var apps = LoadQuickApps();
            apps.Add(new QuickApp { Name = Path.GetFileNameWithoutExtension(path.TrimEnd('/')), Path = path });
            SaveQuickApps(apps);
            RefreshQuickLaunch();
        };
        return button;
    }
}
