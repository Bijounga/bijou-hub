using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using BijouHub.Models;

namespace BijouHub.Mac.Controls;

// "What am I working on?" for a running session: a dropdown of today's open tasks, a ＋ to add a
// new one on the spot, and a ✓ to mark the picked one done. Sits under the timer, in the main
// window and in the pop-out. Same behavior as Windows.
public class SessionTaskPicker : Grid
{
    private static readonly DailyGoal NoTask = new() { Text = "No task" };

    public event Action<DailyGoal?>? TaskPicked;
    public event Action<string>? TaskAdded;
    public event Action? DoneClicked;

    private readonly ComboBox _combo;
    private readonly TextBox _add;
    private readonly Button _plus;
    private readonly Button _done;
    private readonly Button _confirm;
    private bool _updating;
    private bool _adding;
    private bool _doneWanted;

    public SessionTaskPicker()
    {
        ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");

        _combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 60 };
        ToolTip.SetTip(_combo, "The task you're working on");
        Avalonia.Automation.AutomationProperties.SetName(_combo, "Session task");
        _combo.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            TaskPicked?.Invoke(_combo.SelectedItem is DailyGoal goal && goal != NoTask ? goal : null);
        };

        _add = new TextBox { Watermark = "New task, then Enter", IsVisible = false, VerticalContentAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetAutomationId(_add, "SessionTaskAddBox");
        Avalonia.Automation.AutomationProperties.SetName(_add, "New task");
        _add.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitAdd();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SetAdding(false);
                e.Handled = true;
            }
        };

        _plus = IconButton("＋", "Add a task", "Add task");
        _plus.Click += (_, _) => SetAdding(!_adding);
        _done = IconButton("✓", "Mark this task done", "Mark task done");
        _done.IsVisible = false;
        _done.Click += (_, _) => DoneClicked?.Invoke();
        _confirm = IconButton("✓", "Add this task", "Confirm new task");
        _confirm.IsVisible = false;
        _confirm.Click += (_, _) => CommitAdd();

        SetColumn(_combo, 0);
        SetColumn(_add, 0);
        SetColumn(_done, 1);
        SetColumn(_confirm, 1);
        SetColumn(_plus, 2);
        Children.Add(_combo);
        Children.Add(_add);
        Children.Add(_done);
        Children.Add(_confirm);
        Children.Add(_plus);
    }

    private static Button IconButton(string glyph, string tip, string name)
    {
        var button = new Button { Content = glyph, Padding = new Thickness(10, 5), Margin = new Thickness(6, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    private void CommitAdd()
    {
        var text = (_add.Text ?? "").Trim();
        SetAdding(false);
        if (text.Length > 0) TaskAdded?.Invoke(text);
    }

    private void SetAdding(bool on)
    {
        _adding = on;
        _combo.IsVisible = !on;
        _add.IsVisible = on;
        _plus.Content = on ? "✕" : "＋";
        ToolTip.SetTip(_plus, on ? "Cancel" : "Add a task");
        Avalonia.Automation.AutomationProperties.SetName(_plus, on ? "Cancel new task" : "Add task");
        _confirm.IsVisible = on;
        _done.IsVisible = !on && _doneWanted;
        if (on)
        {
            _add.Text = "";
            Dispatcher.UIThread.Post(() => _add.Focus(), DispatcherPriority.Input);
        }
    }

    // The open tasks to choose from, and the one currently picked (null for none).
    public void SetTasks(IReadOnlyList<DailyGoal> tasks, DailyGoal? selected)
    {
        // Don't pull the list out from under the user while the dropdown is open (a pick fires while it still is).
        if (!_combo.IsDropDownOpen)
        {
            _updating = true;
            try
            {
                _combo.ItemsSource = new[] { NoTask }.Concat(tasks).ToList();
                _combo.SelectedItem = selected != null && tasks.Contains(selected) ? selected : NoTask;
            }
            finally
            {
                _updating = false;
            }
        }
        _doneWanted = selected != null && tasks.Contains(selected);
        _done.IsVisible = _doneWanted && !_adding;
    }
}
