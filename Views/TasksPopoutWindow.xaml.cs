using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BijouHub.Services;

namespace BijouHub.Views;

// A small always-on-top task list to keep beside other apps: today's open tasks with the same
// section tabs as Home, or (switch at the top) a day agenda from the calendar. The main window
// fills it in and handles what's done with it.
public partial class TasksPopoutWindow : Window
{
    public event Action<string>? AddRequested;
    public event Action? OpenCalendarRequested;

    public TasksPopoutWindow()
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Top + 16;
        AddBox.TextChanged += (_, _) => AddHint.Visibility = AddBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowCalendarMode(bool calendar)
    {
        TasksScroll.Visibility = calendar ? Visibility.Collapsed : Visibility.Visible;
        AgendaScroll.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
        AddRow.Visibility = calendar ? Visibility.Collapsed : Visibility.Visible;
        OpenCalendarButton.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetEmpty(bool empty) => EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        for (var element = e.OriginalSource as DependencyObject; element != null && element != sender; element = System.Windows.Media.VisualTreeHelper.GetParent(element))
            if (element is System.Windows.Controls.Primitives.ButtonBase) return; // a click on Tasks / Calendar / ✕, not a drag
        DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenCalendar_Click(object sender, RoutedEventArgs e) => OpenCalendarRequested?.Invoke();

    private void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            AddBox.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CommitAdd();
            e.Handled = true;
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => CommitAdd();

    private void CommitAdd()
    {
        var text = AddBox.Text.Trim();
        AddBox.Clear();
        if (text.Length > 0) AddRequested?.Invoke(text);
    }
}
