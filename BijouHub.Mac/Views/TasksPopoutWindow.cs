using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace BijouHub.Mac.Views;

// A small always-on-top task list to keep beside other apps: today's open tasks with the same
// section tabs as Home, or (switch at the top) a day agenda from the calendar. The main window
// fills it in and handles what's done with it. Built in code. Same behavior as Windows.
public class TasksPopoutWindow : Window
{
    public event Action<string>? AddRequested;
    public event Action? OpenCalendarRequested;

    // Set when a saved position was restored, so the default top-right corner isn't applied over it.
    public bool Positioned { get; set; }

    public StackPanel HeaderPills { get; } = new() { Orientation = Orientation.Horizontal };
    public StackPanel TopBar { get; } = new() { Margin = new Thickness(0, 10, 0, 6) };
    public StackPanel TasksHost { get; } = new();
    public StackPanel AgendaHost { get; } = new();

    private readonly ScrollViewer _tasksScroll;
    private readonly ScrollViewer _agendaScroll;
    private readonly TextBlock _empty;
    private readonly Grid _addRow;
    private readonly TextBox _addBox;
    private readonly Button _openCalendar;

    public TasksPopoutWindow()
    {
        Title = "BijouHub Tasks";
        Width = 340;
        Height = 480;
        MinWidth = 260;
        MinHeight = 300;
        Topmost = true;
        CanResize = true;
        ShowInTaskbar = false;
        SystemDecorations = SystemDecorations.BorderOnly;
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (this.TryFindResource("BgBrush", ActualThemeVariant, out var bg) && bg is IBrush bgBrush) Background = bgBrush;

        var close = new Button { Content = "✕", Classes = { "ghost" }, Padding = new Thickness(6, 2) };
        ToolTip.SetTip(close, "Close the task list");
        Avalonia.Automation.AutomationProperties.SetName(close, "Close task list");
        close.Click += (_, _) => Close();
        var header = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(HeaderPills);
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not (Button or TextBlock { Parent: Border or Button })) BeginMoveDrag(e);
        };

        _empty = new TextBlock { Text = "Nothing left here.", FontSize = 12, Margin = new Thickness(0, 6, 0, 0), IsVisible = false, Opacity = 0.6 };
        _tasksScroll = new ScrollViewer { Content = new StackPanel { Children = { TasksHost, _empty } } };
        _agendaScroll = new ScrollViewer { Content = AgendaHost, IsVisible = false };
        var content = new Grid { Children = { _tasksScroll, _agendaScroll } };

        _addBox = new TextBox { Watermark = "＋  Add a task", VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 6) };
        Avalonia.Automation.AutomationProperties.SetAutomationId(_addBox, "PopoutAddBox");
        Avalonia.Automation.AutomationProperties.SetName(_addBox, "Add a task");
        _addBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitAdd(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _addBox.Text = ""; e.Handled = true; }
        };
        var addButton = new Button { Content = "＋", Padding = new Thickness(12, 6), Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(addButton, "Add the task");
        Avalonia.Automation.AutomationProperties.SetName(addButton, "Add task from pop-out");
        addButton.Click += (_, _) => CommitAdd();
        _addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(addButton, 1);
        _addRow.Children.Add(_addBox);
        _addRow.Children.Add(addButton);

        _openCalendar = new Button { Content = "Open in Calendar ›", Padding = new Thickness(10, 6), HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        Avalonia.Automation.AutomationProperties.SetName(_openCalendar, "Open in Calendar");
        _openCalendar.Click += (_, _) => OpenCalendarRequested?.Invoke();

        var footer = new Panel { Margin = new Thickness(0, 8, 0, 0), Children = { _addRow, _openCalendar } };

        var layout = new Grid { Margin = new Thickness(12, 10, 12, 12), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        Grid.SetRow(TopBar, 1);
        Grid.SetRow(content, 2);
        Grid.SetRow(footer, 3);
        layout.Children.Add(header);
        layout.Children.Add(TopBar);
        layout.Children.Add(content);
        layout.Children.Add(footer);

        var frame = new Border { BorderThickness = new Thickness(1.5), Child = layout };
        if (this.TryFindResource("AccentBrush", ActualThemeVariant, out var accent) && accent is IBrush accentBrush) frame.BorderBrush = accentBrush;
        Content = frame;

        Opened += (_, _) =>
        {
            if (!Positioned && Screens.Primary is { } screen)
                Position = new PixelPoint(screen.WorkingArea.Right - (int)(Width * screen.Scaling) - 16, screen.WorkingArea.Y + 16);
        };
    }

    public void ShowCalendarMode(bool calendar)
    {
        _tasksScroll.IsVisible = !calendar;
        _agendaScroll.IsVisible = calendar;
        _addRow.IsVisible = !calendar;
        _openCalendar.IsVisible = calendar;
    }

    public void SetEmpty(bool empty) => _empty.IsVisible = empty;

    private void CommitAdd()
    {
        var text = (_addBox.Text ?? "").Trim();
        _addBox.Text = "";
        if (text.Length > 0) AddRequested?.Invoke(text);
    }
}
