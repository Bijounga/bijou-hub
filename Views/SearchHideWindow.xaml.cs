using System.Windows;
using System.Windows.Controls;
using BijouHub.Services;

namespace BijouHub.Views;

// Pick apps to keep out of Windows search. Ticking one moves its Start menu shortcut aside; unticking
// puts it back. Changes apply at once.
public partial class SearchHideWindow : Window
{
    private readonly StartMenuHider _hider = SearchHiding.Create();

    // One row per app name: the shortcuts that show now, and the ones that are hidden.
    private record AppRow(string Name, List<StartMenuApp> Showing, List<StartMenuApp> Hidden)
    {
        public bool IsHidden => Hidden.Count > 0;
        public bool NeedsPermission => Showing.Concat(Hidden).Any(a => a.Scope == ShortcutScope.Machine);
    }

    private List<AppRow> _rows = new();

    public SearchHideWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Reload();
    }

    private void Reload()
    {
        var hidden = _hider.Hidden();
        var showing = _hider.Visible();
        _rows = showing.Select(a => (a, hide: false)).Concat(hidden.Select(a => (a, hide: true)))
            .GroupBy(x => x.a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AppRow(g.First().a.Name,
                g.Where(x => !x.hide).Select(x => x.a).ToList(),
                g.Where(x => x.hide).Select(x => x.a).ToList()))
            .OrderByDescending(r => r.IsHidden).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ShowAllButton.IsEnabled = hidden.Count > 0;
        Render();
    }

    private void Render()
    {
        var filter = FilterBox.Text.Trim();
        FilterHint.Visibility = string.IsNullOrEmpty(FilterBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        RowsPanel.Children.Clear();
        foreach (var row in _rows.Where(r => filter.Length == 0 || r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            var box = new CheckBox { IsChecked = row.IsHidden, Margin = new Thickness(0, 0, 0, 2), Padding = new Thickness(6, 6, 0, 6), Tag = row };
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock { Text = row.Name, VerticalAlignment = VerticalAlignment.Center });
            var note = row.IsHidden && row.Showing.Count > 0 ? "one copy still shows"
                : row.NeedsPermission ? "asks for permission" : null;
            if (note != null)
                label.Children.Add(new TextBlock
                {
                    Text = "  " + note, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
                });
            box.Content = label;
            // Checked/Unchecked rather than Click, so keyboard and assistive tools toggle it too.
            box.Checked += Row_Toggled;
            box.Unchecked += Row_Toggled;
            System.Windows.Automation.AutomationProperties.SetName(box, $"Hide {row.Name} from search");
            RowsPanel.Children.Add(box);
        }
        if (RowsPanel.Children.Count == 0)
            RowsPanel.Children.Add(new TextBlock
            {
                Text = filter.Length == 0 ? "No apps found in the Start menu." : "Nothing matches that.",
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"), Margin = new Thickness(2, 8, 0, 0),
            });
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) { if (RowsPanel != null) Render(); }

    private void Row_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: AppRow row } box) return;
        // Redraw after this handler returns: it's one of the boxes that gets replaced.
        Dispatcher.BeginInvoke(() => { if (box.IsChecked == true) Hide(row); else Show(row); });
    }

    private void Hide(AppRow row)
    {
        var outcome = _hider.Hide(row.Showing);
        var asked = false;
        var declined = false;
        if (outcome.NeedsAdmin.Count > 0)
        {
            asked = true;
            var result = SearchHiding.RunElevated(outcome.NeedsAdmin.Select(a => new StartMenuHider.PlanStep(true, a.Scope, a.RelativePath)).ToList());
            declined = result == null;
        }
        Reload();
        var left = _rows.FirstOrDefault(r => r.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
        if (left != null && left.Showing.Count > 0)
            StatusText.Text = declined
                ? $"{row.Name} is still in search through the shared Start menu: Windows needs your permission to move that one."
                : asked ? $"{row.Name} couldn't be fully hidden. Try again." : $"{row.Name} couldn't be hidden.";
        else
            StatusText.Text = $"{row.Name} is hidden. Windows search can take a minute to catch up.";
    }

    private void Show(AppRow row)
    {
        var outcome = _hider.Restore(row.Hidden);
        var declined = false;
        if (outcome.NeedsAdmin.Count > 0)
            declined = SearchHiding.RunElevated(outcome.NeedsAdmin.Select(a => new StartMenuHider.PlanStep(false, a.Scope, a.RelativePath)).ToList()) == null;
        Reload();
        StatusText.Text = declined ? $"{row.Name} is still hidden: Windows needs your permission to put the shared shortcut back."
            : $"{row.Name} will show in search again.";
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        var all = _hider.Hidden();
        var outcome = _hider.Restore(all);
        var declined = false;
        if (outcome.NeedsAdmin.Count > 0)
            declined = SearchHiding.RunElevated(outcome.NeedsAdmin.Select(a => new StartMenuHider.PlanStep(false, a.Scope, a.RelativePath)).ToList()) == null;
        Reload();
        StatusText.Text = declined ? "Some shared shortcuts are still hidden: Windows needs your permission to put them back."
            : "Everything shows in search again.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
