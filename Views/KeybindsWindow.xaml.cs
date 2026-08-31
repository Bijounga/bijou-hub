using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BijouHub.Services;

namespace BijouHub.Views;

public partial class KeybindsWindow : Window
{
    private readonly KeybindStore _store;
    public event Action? KeybindsChanged;

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["Bold"] = "Bold",
        ["Italic"] = "Italic",
        ["Heading"] = "Heading",
        ["BulletList"] = "Bullet list",
        ["Checklist"] = "Checklist"
    };

    public KeybindsWindow(KeybindStore store)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _store = store;
        BuildRows();
    }

    private void BuildRows()
    {
        RowsPanel.Children.Clear();
        foreach (var action in KeybindStore.Defaults.Keys)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            var label = new TextBlock { Text = Labels[action], VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox { Text = _store.Get(action), IsReadOnly = true, Cursor = Cursors.Hand, Tag = action };
            box.PreviewKeyDown += KeybindBox_PreviewKeyDown;

            Grid.SetColumn(label, 0);
            Grid.SetColumn(box, 1);
            row.Children.Add(label);
            row.Children.Add(box);
            RowsPanel.Children.Add(row);
        }
    }

    private void KeybindBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: string action } box) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        var gesture = string.Join("+", parts);

        if (KeybindStore.ParseGesture(gesture) == null) return;

        box.Text = gesture;
        _store.Set(action, gesture);
        KeybindsChanged?.Invoke();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        foreach (var kv in KeybindStore.Defaults)
            _store.Set(kv.Key, kv.Value);
        BuildRows();
        KeybindsChanged?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
