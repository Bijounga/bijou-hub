using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BijouHub.Mac.Controls;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

// A black screen with just the timer in the middle and the plan in small type under it, to leave on
// a second monitor without it pulling your eye. Move the mouse and the controls appear; leave it
// alone and they (and the cursor) go away. Esc goes back. Built in code. Same behavior as Windows.
public class BlackoutWindow : Window
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
        ("Theme font", null), ("Inter", "fonts:Inter#Inter"), ("Avenir Next", "Avenir Next"), ("Helvetica Neue", "Helvetica Neue"),
        ("Menlo", "Menlo"), ("Monaco", "Monaco"), ("Palatino", "Palatino"), ("Georgia", "Georgia"),
        ("Courier New", "Courier New"), ("Impact", "Impact")
    };

    private FocusLook _look = new();
    private bool _applying;
    private readonly FontFamily _themeFont;
    private readonly IBrush? _themeBrush;
    private readonly Viewbox _timeBox = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    private readonly ComboBox _fontBox = new() { Width = 170, Margin = new Thickness(14, 0, 14, 0) };
    private readonly StackPanel _swatches = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _screenButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };

    private readonly TextBlock _time = new() { Text = "00:00:00", FontSize = 200, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.85 };
    private readonly TextBlock _goal = new() { FontSize = 20, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 900, Margin = new Thickness(0, 14, 0, 0), IsVisible = false, Foreground = new SolidColorBrush(Color.Parse("#8C96A3")) };
    private readonly TextBlock _status = new() { FontSize = 14, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Foreground = new SolidColorBrush(Color.Parse("#4C5560")) };
    private readonly Button _pause = new() { Content = "Pause", Padding = new Thickness(12, 6) };
    private readonly SessionTaskPicker _picker = new() { Width = 320, Margin = new Thickness(0, 0, 14, 0) };
    private readonly Border _controls = new();
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(3) };
    private Point _lastMouse;

    public BlackoutWindow()
    {
        Title = "BijouHub Focus Screen";
        Background = Brushes.Black;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 800;
        Height = 450;
        if (this.TryFindResource("TimerFont", ActualThemeVariant, out var font) && font is FontFamily family) _time.FontFamily = family;
        if (this.TryFindResource("AccentBrush", ActualThemeVariant, out var accent) && accent is IBrush accentBrush) _time.Foreground = accentBrush;

        // remembered so the theme's own timer font and color can be put back
        _themeFont = _time.FontFamily;
        _themeBrush = _time.Foreground;
        _timeBox.Child = _time;

        var center = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false,
            Children =
            {
                _timeBox,
                _goal,
                _status
            }
        };

        Button Make(string text, string name, Action click, string? tip = null)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 6), Margin = new Thickness(8, 0, 0, 0) };
            Avalonia.Automation.AutomationProperties.SetName(button, name);
            if (tip != null) ToolTip.SetTip(button, tip);
            button.Click += (_, _) => click();
            return button;
        }
        Avalonia.Automation.AutomationProperties.SetName(_pause, "Blackout pause");
        _pause.Click += (_, _) => PauseRequested?.Invoke();
        _picker.TaskPicked += goal => TaskPicked?.Invoke(goal);
        _picker.TaskAdded += text => TaskAdded?.Invoke(text);
        _picker.DoneClicked += () => TaskDone?.Invoke();

        _controls.Padding = new Thickness(14, 10);
        _controls.CornerRadius = new CornerRadius(10);
        _controls.Background = new SolidColorBrush(Color.Parse("#14181D"));
        _controls.BorderBrush = new SolidColorBrush(Color.Parse("#232A33"));
        _controls.BorderThickness = new Thickness(1);
        _controls.VerticalAlignment = VerticalAlignment.Bottom;
        _controls.HorizontalAlignment = HorizontalAlignment.Center;
        _controls.Margin = new Thickness(0, 0, 0, 40);
        Avalonia.Automation.AutomationProperties.SetName(_fontBox, "Blackout font");
        ToolTip.SetTip(_fontBox, "Timer font");
        foreach (var (label, _) in Fonts) _fontBox.Items.Add(label);
        _fontBox.SelectionChanged += (_, _) =>
        {
            if (_applying || _fontBox.SelectedIndex < 0) return;
            var index = _fontBox.SelectedIndex;
            Change(l => l.Font = index == 0 ? null : Fonts[index].Label);
        };
        AddSwatch("Theme color", null);
        foreach (var (name, hex) in FocusLook.Colors) AddSwatch(name, hex);

        _controls.Child = new StackPanel
        {
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        _picker, _pause,
                        Make("Finish", "Blackout finish", () => FinishRequested?.Invoke()),
                        _screenButtons,
                        Make("Exit", "Blackout exit", () => ExitRequested?.Invoke(), "Back to BijouHub (Esc)")
                    }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
                    Children =
                    {
                        Make("A−", "Blackout smaller", () => Change(l => l.Scale = FocusLook.Step(l.Scale, -1)), "Smaller timer (-, or scroll down)"),
                        Make("A+", "Blackout bigger", () => Change(l => l.Scale = FocusLook.Step(l.Scale, 1)), "Bigger timer (+, or scroll up)"),
                        _fontBox, _swatches
                    }
                }
            }
        };

        Content = new Grid { Background = Brushes.Black, Children = { center, _controls } };

        _idle.Tick += (_, _) => HideControls();
        Opened += (_, _) => HideControls();
        PointerMoved += (_, e) =>
        {
            var at = e.GetPosition(this);
            if (Math.Abs(at.X - _lastMouse.X) + Math.Abs(at.Y - _lastMouse.Y) < 3) return; // a twitch isn't a move
            _lastMouse = at;
            ShowControls();
        };
        PointerPressed += (_, _) => ShowControls();
        PointerWheelChanged += (_, e) =>
        {
            if (e.Delta.Y == 0) return;
            Change(l => l.Scale = FocusLook.Step(l.Scale, e.Delta.Y > 0 ? 1 : -1));
            ShowControls();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_controls.IsKeyboardFocusWithin) FocusManager?.ClearFocus();
                else ExitRequested?.Invoke();
                e.Handled = true;
            }
            else if (!_controls.IsKeyboardFocusWithin && e.Key is Key.OemPlus or Key.Add) { Change(l => l.Scale = FocusLook.Step(l.Scale, 1)); ShowControls(); e.Handled = true; }
            else if (!_controls.IsKeyboardFocusWithin && e.Key is Key.OemMinus or Key.Subtract) { Change(l => l.Scale = FocusLook.Step(l.Scale, -1)); ShowControls(); e.Handled = true; }
            else ShowControls();
        };
    }

    // The monitors to move to, left to right (what each is, for its tooltip), and which one this is on.
    public void SetScreens(IReadOnlyList<string> descriptions, int current)
    {
        _screenButtons.Children.Clear();
        _screenButtons.Children.Add(new TextBlock { Text = "Screen", Foreground = new SolidColorBrush(Color.Parse("#8C96A3")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) });
        for (var i = 0; i < descriptions.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = (i + 1).ToString(), Padding = new Thickness(11, 6), Margin = new Thickness(0, 0, 4, 0),
                BorderBrush = i == current && this.TryFindResource("AccentBrush", ActualThemeVariant, out var accent) && accent is IBrush accentBrush ? accentBrush : new SolidColorBrush(Color.Parse("#323A45")),
                BorderThickness = new Thickness(i == current ? 2 : 1)
            };
            ToolTip.SetTip(button, descriptions[i]);
            Avalonia.Automation.AutomationProperties.SetName(button, $"Blackout screen {i + 1}");
            button.Click += (_, _) => ScreenChosen?.Invoke(index);
            _screenButtons.Children.Add(button);
        }
    }

    // How the timer looks (set before showing).
    public void SetLook(FocusLook look)
    {
        _look = new FocusLook { Scale = Math.Clamp(look.Scale, FocusLook.MinScale, FocusLook.MaxScale), Font = look.Font, Color = look.Color };
        ApplyLook();
    }

    private void ApplyLook()
    {
        _applying = true;
        _time.FontSize = 200 * _look.Scale;
        FitTimer();

        var index = Array.FindIndex(Fonts, f => f.Label == _look.Font);
        if (index < 0) index = 0;
        _time.FontFamily = Fonts[index].Family is { } family ? new FontFamily(family) : _themeFont;
        _fontBox.SelectedIndex = index;

        _time.Foreground = _look.Color != null && Color.TryParse(_look.Color, out var color) ? new SolidColorBrush(color) : _themeBrush;
        foreach (Button swatch in _swatches.Children)
            swatch.BorderBrush = (string?)swatch.Tag == _look.Color ? Brushes.White : new SolidColorBrush(Color.Parse("#323A45"));
        _applying = false;
    }

    // Big timers still have to fit the screen, with room left for the plan under them.
    private void FitTimer()
    {
        _timeBox.MaxWidth = Math.Max(200, Bounds.Width - 80);
        _timeBox.MaxHeight = Math.Max(100, Bounds.Height * 0.62);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) FitTimer();
    }

    private void AddSwatch(string name, string? hex)
    {
        var swatch = new Button
        {
            Width = 24, Height = 24, MinHeight = 0, MinWidth = 0, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(0), Tag = hex,
            Background = hex == null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hex)), BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Content = hex == null ? new TextBlock { Text = "A", FontSize = 11, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center } : null
        };
        ToolTip.SetTip(swatch, name);
        Avalonia.Automation.AutomationProperties.SetName(swatch, "Blackout color " + name);
        swatch.Click += (_, _) => Change(l => l.Color = hex);
        _swatches.Children.Add(swatch);
    }

    private void Change(Action<FocusLook> edit)
    {
        edit(_look);
        ApplyLook();
        LookChanged?.Invoke(new FocusLook { Scale = _look.Scale, Font = _look.Font, Color = _look.Color });
    }

    public void Update(string time, string status, bool paused, string? goal)
    {
        _time.Text = time;
        _status.Text = status;
        _pause.Content = paused ? "Resume" : "Pause";
        _goal.Text = goal ?? "";
        _goal.IsVisible = !string.IsNullOrWhiteSpace(goal);
    }

    public void SetTasks(IReadOnlyList<DailyGoal> tasks, DailyGoal? selected) => _picker.SetTasks(tasks, selected);

    private void ShowControls()
    {
        _controls.Opacity = 1;
        _controls.IsHitTestVisible = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
        _idle.Stop();
        _idle.Start();
    }

    private void HideControls()
    {
        _idle.Stop();
        // Leave them up while a dropdown is open or a task is being typed.
        if (_controls.IsKeyboardFocusWithin) { _idle.Start(); return; }
        _controls.Opacity = 0;
        _controls.IsHitTestVisible = false;
        Cursor = new Cursor(StandardCursorType.None);
    }
}
