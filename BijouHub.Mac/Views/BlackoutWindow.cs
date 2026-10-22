using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BijouHub.Mac.Controls;
using BijouHub.Models;

namespace BijouHub.Mac.Views;

// A black screen with just the timer in the middle and the plan in small type under it, to leave on
// a second monitor without it pulling your eye. Move the mouse and the controls appear; leave it
// alone and they (and the cursor) go away. Esc goes back. Built in code. Same behavior as Windows.
public class BlackoutWindow : Window
{
    public event Action? ExitRequested;
    public event Action? PauseRequested;
    public event Action? FinishRequested;
    public event Action? NextScreenRequested;
    public event Action<DailyGoal?>? TaskPicked;
    public event Action<string>? TaskAdded;
    public event Action? TaskDone;

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

        var center = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false,
            Children =
            {
                new Viewbox { MaxWidth = 1400, MaxHeight = 360, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = _time },
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
        _controls.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _picker, _pause,
                Make("Finish", "Blackout finish", () => FinishRequested?.Invoke()),
                Make("Other screen", "Blackout other screen", () => NextScreenRequested?.Invoke(), "Move this to another monitor"),
                Make("Exit", "Blackout exit", () => ExitRequested?.Invoke(), "Back to BijouHub (Esc)")
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
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_controls.IsKeyboardFocusWithin) FocusManager?.ClearFocus();
                else ExitRequested?.Invoke();
                e.Handled = true;
            }
            else ShowControls();
        };
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
