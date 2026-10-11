using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BijouHub.Mac.Views;

// A small always-on-top timer to keep in a corner while working in another app.
public partial class TimerPopoutWindow : Window
{
    public event Action? PauseRequested;
    public event Action? FinishRequested;
    public event Action<BijouHub.Models.DailyGoal?>? TaskPicked;
    public event Action<string>? TaskAdded;
    public event Action? TaskDone;

    public TimerPopoutWindow()
    {
        InitializeComponent();
        TaskPicker.TaskPicked += goal => TaskPicked?.Invoke(goal);
        TaskPicker.TaskAdded += text => TaskAdded?.Invoke(text);
        TaskPicker.DoneClicked += () => TaskDone?.Invoke();
    }

    public void SetTasks(IReadOnlyList<BijouHub.Models.DailyGoal> tasks, BijouHub.Models.DailyGoal? selected) => TaskPicker.SetTasks(tasks, selected);

    public void Update(string title, string time, string status)
    {
        TitleText.Text = title;
        TimeText.Text = time;
        StatusText.Text = status;
    }

    private void Drag_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button) BeginMoveDrag(e);
    }

    private void Dock_Click(object? sender, RoutedEventArgs e) => Close();
    private void Pause_Click(object? sender, RoutedEventArgs e) => PauseRequested?.Invoke();
    private void Finish_Click(object? sender, RoutedEventArgs e) => FinishRequested?.Invoke();
}
