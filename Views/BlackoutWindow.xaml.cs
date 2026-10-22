using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BijouHub.Models;

namespace BijouHub.Views;

// A black screen with just the timer in the middle and the plan in small type under it, to leave on
// a second monitor without it pulling your eye. Move the mouse and the controls appear; leave it
// alone and they (and the cursor) go away. Esc goes back.
public partial class BlackoutWindow : Window
{
    public event Action? ExitRequested;
    public event Action? PauseRequested;
    public event Action? FinishRequested;
    public event Action? NextScreenRequested;
    public event Action<DailyGoal?>? TaskPicked;
    public event Action<string>? TaskAdded;
    public event Action? TaskDone;

    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(3) };
    private Point _lastMouse;

    public BlackoutWindow()
    {
        InitializeComponent();
        TaskPicker.TaskPicked += goal => TaskPicked?.Invoke(goal);
        TaskPicker.TaskAdded += text => TaskAdded?.Invoke(text);
        TaskPicker.DoneClicked += () => TaskDone?.Invoke();
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
        else ShowControls();
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => PauseRequested?.Invoke();
    private void Finish_Click(object sender, RoutedEventArgs e) => FinishRequested?.Invoke();
    private void NextScreen_Click(object sender, RoutedEventArgs e) => NextScreenRequested?.Invoke();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}
