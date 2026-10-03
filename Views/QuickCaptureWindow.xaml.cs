using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace BijouHub.Views;

// A small Spotlight-style box for getting a task out of your head without leaving what you're
// in: opened from the Stream Deck's Quick Capture key, Enter adds it to today's goals, Esc or
// clicking away closes it.
public partial class QuickCaptureWindow : Window
{
    public event Action<string, object?>? Captured;

    public QuickCaptureWindow(IEnumerable<(string Label, object Tag)> lists, object? selected)
    {
        InitializeComponent();
        foreach (var (label, tag) in lists)
            ListCombo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        ListCombo.SelectedItem = ListCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, selected))
                                 ?? ListCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        ListCombo.Visibility = ListCombo.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ListCombo.DropDownClosed += (_, _) => TaskBox.Focus();

        // A third of the way down the main screen, like a launcher.
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + area.Height * 0.26;
            TakeFocus();
        };
        // Closing hands focus back to the app underneath, which deactivates this box mid-close;
        // closing it a second time then would throw (and take BijouHub down with it).
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) =>
        {
            if (!_closing && !ListCombo.IsDropDownOpen) Close();
        };
    }

    private bool _closing;

    private void TaskBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = TaskBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void TaskBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (!_closing) Close();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Submit();
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e) => Submit();

    private void Submit()
    {
        var text = TaskBox.Text.Trim();
        if (text.Length == 0)
        {
            TaskBox.Focus();
            return;
        }
        Captured?.Invoke(text, (ListCombo.SelectedItem as ComboBoxItem)?.Tag);
        if (!_closing) Close();
    }

    // The key press happens on the Stream Deck, so another app is in front: borrow its input
    // queue for a moment so Windows lets this box take the keyboard.
    private void TakeFocus()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        var ownThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            SetForegroundWindow(handle);
            Activate();
        }
        finally
        {
            if (attached) AttachThreadInput(ownThread, foregroundThread, false);
        }
        TaskBox.Focus();
        Keyboard.Focus(TaskBox);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
