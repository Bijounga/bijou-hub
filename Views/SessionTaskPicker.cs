using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BijouHub.Models;

namespace BijouHub.Views;

// "What am I working on?" for a running session: a dropdown of today's open tasks, a ＋ to add a
// new one on the spot, and a ✓ to mark the picked one done. Sits under the timer, in the main
// window and in the pop-out. Built in code so it needs no XAML of its own and takes the app's theme.
public class SessionTaskPicker : Grid
{
    private static readonly DailyGoal NoTask = new() { Text = "No task" };

    public event Action<DailyGoal?>? TaskPicked;
    public event Action<string>? TaskAdded;
    public event Action? DoneClicked;

    private readonly ComboBox _combo;
    private readonly Grid _addBox;
    private readonly TextBox _add;
    private readonly TextBlock _hint;
    private readonly Button _plus;
    private readonly Button _done;
    private readonly Button _confirm;
    private bool _updating;
    private bool _adding;
    private bool _doneWanted;

    public SessionTaskPicker()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _combo = new ComboBox { DisplayMemberPath = nameof(DailyGoal.Text), MinWidth = 60, ToolTip = "The task you're working on" };
        System.Windows.Automation.AutomationProperties.SetName(_combo, "Session task");
        _combo.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            TaskPicked?.Invoke(_combo.SelectedItem is DailyGoal goal && goal != NoTask ? goal : null);
        };

        _add = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 3, 6, 3) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_add, "SessionTaskAddBox");
        System.Windows.Automation.AutomationProperties.SetName(_add, "New task");
        _hint = new TextBlock { Text = "New task, then Enter", IsHitTestVisible = false, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        _addBox = new Grid { Visibility = Visibility.Collapsed, Children = { _add, _hint } };
        _add.TextChanged += (_, _) => _hint.Visibility = _add.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        _confirm = IconButton("✓", "Add this task", "Confirm new task");
        _confirm.Visibility = Visibility.Collapsed;
        _confirm.Click += (_, _) => CommitAdd();
        _done = IconButton("✓", "Mark this task done", "Mark task done");
        _done.Visibility = Visibility.Collapsed;
        _done.Click += (_, _) => DoneClicked?.Invoke();

        SetColumn(_combo, 0);
        SetColumn(_addBox, 0);
        SetColumn(_done, 1);
        SetColumn(_confirm, 1);
        SetColumn(_plus, 2);
        Children.Add(_combo);
        Children.Add(_addBox);
        Children.Add(_done);
        Children.Add(_confirm);
        Children.Add(_plus);
    }

    private void CommitAdd()
    {
        var text = _add.Text.Trim();
        SetAdding(false);
        if (text.Length > 0) TaskAdded?.Invoke(text);
    }

    private static Button IconButton(string glyph, string tip, string name)
    {
        var button = new Button { Content = glyph, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = tip, Focusable = true };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    private void SetAdding(bool on)
    {
        _adding = on;
        _combo.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        _addBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _plus.Content = on ? "✕" : "＋";
        _plus.ToolTip = on ? "Cancel" : "Add a task";
        System.Windows.Automation.AutomationProperties.SetName(_plus, on ? "Cancel new task" : "Add task");
        _confirm.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _done.Visibility = on ? Visibility.Collapsed : _doneWanted ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            _add.Clear();
            _hint.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(() => _add.Focus(), System.Windows.Threading.DispatcherPriority.Input);
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
        _done.Visibility = _doneWanted && !_adding ? Visibility.Visible : Visibility.Collapsed;
    }
}
