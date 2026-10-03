using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BijouHub.Mac.Views;

// A small Spotlight-style box for getting a task out of your head without leaving what you're
// in: opened from the Stream Deck's Quick Capture key, Return adds it to today's goals, Esc or
// clicking away closes it.
public partial class QuickCaptureWindow : Window
{
    public event Action<string, object?>? Captured;

    public QuickCaptureWindow() : this(Array.Empty<(string, object)>(), null) { }

    public QuickCaptureWindow(IEnumerable<(string Label, object Tag)> lists, object? selected)
    {
        InitializeComponent();
        foreach (var (label, tag) in lists)
            ListCombo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        var items = ListCombo.Items.OfType<ComboBoxItem>().ToList();
        ListCombo.SelectedItem = items.FirstOrDefault(i => Equals(i.Tag, selected)) ?? items.FirstOrDefault();
        ListCombo.IsVisible = items.Count > 1;
        ListCombo.DropDownClosed += (_, _) => TaskBox.Focus();

        // A quarter of the way down the main screen, like a launcher.
        Opened += (_, _) =>
        {
            if (Screens.Primary is { } screen)
            {
                var area = screen.WorkingArea;
                var width = (int)(Bounds.Width * screen.Scaling);
                Position = new PixelPoint(area.X + (area.Width - width) / 2, area.Y + (int)(area.Height * 0.26));
            }
            Activate(); // on a Mac this also brings BijouHub forward, so the box gets the keyboard
            TaskBox.Focus();
        };
        Deactivated += (_, _) =>
        {
            if (!ListCombo.IsDropDownOpen) Close();
        };
    }

    private void TaskBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Submit();
        }
    }

    private void Add_Click(object? sender, RoutedEventArgs e) => Submit();

    private void Submit()
    {
        var text = (TaskBox.Text ?? "").Trim();
        if (text.Length == 0)
        {
            TaskBox.Focus();
            return;
        }
        Captured?.Invoke(text, (ListCombo.SelectedItem as ComboBoxItem)?.Tag);
        Close();
    }
}
