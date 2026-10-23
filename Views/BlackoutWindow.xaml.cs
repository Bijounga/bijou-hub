using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Views;

// A black screen with just the timer in the middle and the plan in small type under it, to leave on
// a second monitor without it pulling your eye. Move the mouse and the controls appear; leave it
// alone and they (and the cursor) go away. Esc goes back.
public partial class BlackoutWindow : Window
{
    public event Action? ExitRequested;
    public event Action? PauseRequested;
    public event Action? FinishRequested;
    public event Action<int>? ScreenChosen;
    public event Action<DailyGoal?>? TaskPicked;
    public event Action<string>? TaskAdded;
    public event Action? TaskDone;
    public event Action<FocusLook>? LookChanged;

    // The fonts on offer: what's shown, and the font family that goes with it. The first is the theme's own.
    private static readonly (string Label, string? Family)[] Fonts =
    {
        ("Theme font", null), ("Segoe UI Light", "Segoe UI Light"), ("Segoe UI", "Segoe UI"), ("Segoe UI Semibold", "Segoe UI Semibold"),
        ("Bahnschrift", "Bahnschrift"), ("Consolas", "Consolas"), ("Cascadia Mono", "Cascadia Mono"), ("Georgia", "Georgia"),
        ("Courier New", "Courier New"), ("Impact", "Impact"),
        ("Rajdhani", "pack://application:,,,/Fonts/#Rajdhani SemiBold"), ("IBM Plex Mono", "pack://application:,,,/Fonts/#IBM Plex Mono")
    };

    private FocusLook _look = new();
    private bool _applying;

    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(3) };
    private Point _lastMouse;

    public BlackoutWindow()
    {
        InitializeComponent();
        TaskPicker.TaskPicked += goal => TaskPicked?.Invoke(goal);
        TaskPicker.TaskAdded += text => TaskAdded?.Invoke(text);
        TaskPicker.DoneClicked += () => TaskDone?.Invoke();
        foreach (var (label, _) in Fonts) FontBox.Items.Add(label);
        AddSwatch("Theme color", null);
        foreach (var (name, hex) in FocusLook.Colors) AddSwatch(name, hex);
        _idle.Tick += (_, _) => HideControls();
        Loaded += (_, _) => HideControls();
    }

    public void Update(string time, string status, bool paused, string? goal)
    {
        TimeText.Text = time;
        StatusText.Text = status;
        PauseButton.Content = paused ? "Resume" : "Pause";
        GoalText.Text = goal ?? "";
        GoalText.Visibility = string.IsNullOrWhiteSpace(goal) ? Visibility.Collapsed : Visibility.Visible;
    }

    // The monitors to move to, left to right (what each is, for its tooltip), and which one this is on.
    public void SetScreens(IReadOnlyList<string> descriptions, int current)
    {
        ScreenButtons.Children.Clear();
        ScreenButtons.Children.Add(new TextBlock { Text = "Screen", Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x96, 0xA3)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) });
        for (var i = 0; i < descriptions.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = (i + 1).ToString(), Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(0, 0, 4, 0), ToolTip = descriptions[i],
                BorderBrush = i == current ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromRgb(0x32, 0x3A, 0x45)),
                BorderThickness = new Thickness(i == current ? 2 : 1)
            };
            AutomationProperties.SetName(button, $"Blackout screen {i + 1}");
            button.Click += (_, _) => ScreenChosen?.Invoke(index);
            ScreenButtons.Children.Add(button);
        }
    }

    // How the timer looks (set before showing, and again whenever it changes).
    public void SetLook(FocusLook look)
    {
        _look = new FocusLook { Scale = Math.Clamp(look.Scale, FocusLook.MinScale, FocusLook.MaxScale), Font = look.Font, Color = look.Color };
        ApplyLook();
    }

    private void ApplyLook()
    {
        _applying = true;
        TimeText.FontSize = 200 * _look.Scale;
        FitTimer();

        var font = Array.Find(Fonts, f => f.Label == _look.Font);
        if (font.Family == null)
        {
            TimeText.SetResourceReference(TextBlock.FontFamilyProperty, "TimerFont");
            TimeText.SetResourceReference(TextBlock.FontWeightProperty, "TimerFontWeight");
        }
        else
        {
            TimeText.FontFamily = new FontFamily(font.Family);
            TimeText.FontWeight = FontWeights.Normal;
        }
        FontBox.SelectedIndex = font.Label == null ? 0 : Array.IndexOf(Fonts, font);

        if (_look.Color != null && TryBrush(_look.Color) is { } brush) TimeText.Foreground = brush;
        else TimeText.SetResourceReference(TextBlock.ForegroundProperty, "TimerTextBrush");
        foreach (Button swatch in Swatches.Children)
            swatch.BorderBrush = (string?)swatch.Tag == _look.Color ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x32, 0x3A, 0x45));
        _applying = false;
    }

    private static Brush? TryBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return null; }
    }

    // Big timers still have to fit the screen, with room left for the plan under them.
    private void FitTimer()
    {
        TimeBox.MaxWidth = Math.Max(200, ActualWidth - 80);
        TimeBox.MaxHeight = Math.Max(100, ActualHeight * 0.62);
    }

    private void AddSwatch(string name, string? hex)
    {
        var swatch = new Button
        {
            Width = 24, Height = 24, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(0), Tag = hex,
            Background = hex == null ? Brushes.Transparent : TryBrush(hex), BorderThickness = new Thickness(2),
            ToolTip = name, Content = hex == null ? new TextBlock { Text = "A", FontSize = 11, Foreground = Brushes.Gray } : null
        };
        AutomationProperties.SetName(swatch, "Blackout color " + name);
        swatch.Click += (_, _) => Change(l => l.Color = hex);
        Swatches.Children.Add(swatch);
    }

    private void Change(Action<FocusLook> edit)
    {
        edit(_look);
        ApplyLook();
        LookChanged?.Invoke(new FocusLook { Scale = _look.Scale, Font = _look.Font, Color = _look.Color });
    }

    private void Bigger_Click(object sender, RoutedEventArgs e) => Change(l => l.Scale = FocusLook.Step(l.Scale, 1));
    private void Smaller_Click(object sender, RoutedEventArgs e) => Change(l => l.Scale = FocusLook.Step(l.Scale, -1));
    private void Font_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || FontBox.SelectedIndex < 0) return;
        var label = Fonts[FontBox.SelectedIndex].Label;
        Change(l => l.Font = FontBox.SelectedIndex == 0 ? null : label);
    }
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => FitTimer();
    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0) return;
        Change(l => l.Scale = FocusLook.Step(l.Scale, e.Delta > 0 ? 1 : -1));
        ShowControls();
    }

    public void SetTasks(IReadOnlyList<DailyGoal> tasks, DailyGoal? selected) => TaskPicker.SetTasks(tasks, selected);

    private void ShowControls()
    {
        Controls.Opacity = 1;
        Controls.IsHitTestVisible = true;
        Cursor = Cursors.Arrow;
        _idle.Stop();
        _idle.Start();
    }

    private void HideControls()
    {
        _idle.Stop();
        // Leave them up while a dropdown is open or a task is being typed.
        if (Controls.IsKeyboardFocusWithin) { _idle.Start(); return; }
        Controls.Opacity = 0;
        Controls.IsHitTestVisible = false;
        Cursor = Cursors.None;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - _lastMouse.X) + Math.Abs(at.Y - _lastMouse.Y) < 3) return; // a twitch isn't a move
        _lastMouse = at;
        ShowControls();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e) => ShowControls();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !Controls.IsKeyboardFocusWithin) { ExitRequested?.Invoke(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Keyboard.ClearFocus(); e.Handled = true; }
        else if (!Controls.IsKeyboardFocusWithin && e.Key is Key.OemPlus or Key.Add) { Bigger_Click(this, new RoutedEventArgs()); ShowControls(); e.Handled = true; }
        else if (!Controls.IsKeyboardFocusWithin && e.Key is Key.OemMinus or Key.Subtract) { Smaller_Click(this, new RoutedEventArgs()); ShowControls(); e.Handled = true; }
        else ShowControls();
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => PauseRequested?.Invoke();
    private void Finish_Click(object sender, RoutedEventArgs e) => FinishRequested?.Invoke();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}
