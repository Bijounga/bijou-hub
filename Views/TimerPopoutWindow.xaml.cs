using System.Windows;
using BijouHub.Services;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BijouHub.Views;

public partial class TimerPopoutWindow : Window
{
    public event Action? DockRequested;
    public event Action? FinishRequested;
    public event Action? FocusScreenRequested;
    public event Action<BijouHub.Models.DailyGoal?>? TaskPicked;
    public event Action<string>? TaskAdded;
    public event Action? TaskDone;

    public TimerPopoutWindow()
    {
        InitializeComponent();
        ShowTaskbarState();
        TaskPicker.TaskPicked += goal => TaskPicked?.Invoke(goal);
        TaskPicker.TaskAdded += text => TaskAdded?.Invoke(text);
        TaskPicker.DoneClicked += () => TaskDone?.Invoke();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Top + 16;
    }

    public void SetTasks(IReadOnlyList<BijouHub.Models.DailyGoal> tasks, BijouHub.Models.DailyGoal? selected) => TaskPicker.SetTasks(tasks, selected);

    public void UpdateDisplay(string name, string timerText, string statusText)
    {
        NameText.Text = name;
        TimerText.Text = timerText;
        StatusText.Text = statusText;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (IsDescendantOf(e.OriginalSource as DependencyObject, ResizeThumb)) return;
        DragMove();
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject ancestor)
    {
        while (element != null)
        {
            if (element == ancestor) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void Close_Click(object sender, MouseButtonEventArgs e) => DockRequested?.Invoke();

    private void Dock_Click(object sender, RoutedEventArgs e) => DockRequested?.Invoke();

    private void Finish_Click(object sender, RoutedEventArgs e) => FinishRequested?.Invoke();

    // The Windows taskbar: hidden until the pointer goes to the screen edge, or always showing.
    private void Taskbar_Click(object sender, RoutedEventArgs e)
    {
        TaskbarState.AutoHide = !TaskbarState.AutoHide;
        ShowTaskbarState();
    }

    private void ShowTaskbarState()
    {
        var hidden = TaskbarState.AutoHide;
        TaskbarIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, hidden ? "AccentBrush" : "TextBrush");
        TaskbarButton.ToolTip = hidden
            ? "The taskbar is hidden until you point at the screen edge. Click to show it again"
            : "Hide the taskbar (point at the screen edge to bring it back)";
    }

    private void FocusScreen_Click(object sender, RoutedEventArgs e) => FocusScreenRequested?.Invoke();
}
