using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Views;

namespace BijouHub;

// Picks the look of a sidebar row: channel headers are plain section titles, projects use the
// theme's normal list-row look.
public sealed class SidebarRowStyleSelector : StyleSelector
{
    public Style? HeaderStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container) => item is ChannelHeader ? HeaderStyle : null;
}

// Channels (YouTube channels, or any buckets): the sidebar groups projects under them, the Home
// board filters by them, and a color follows each project into its cards and tasks.
public partial class MainWindow
{
    private readonly ChannelStore _channelStore = new();
    private List<Channel> _channels = new();
    private HashSet<string> _collapsedChannels = new();
    private string _boardFilter = "";
    private bool _rebuildingProjectRows;

    private void InitChannels()
    {
        var settings = _settingsStore.Load();
        _collapsedChannels = settings.CollapsedChannels.ToHashSet();
        _boardFilter = settings.BoardChannelFilter ?? "";
        _channels = _channelStore.Load();
        ProjectsList.ContextMenu = new ContextMenu(); // filled when it opens (it depends on what's under the cursor)
        RebuildProjectRows();
    }

    private void ReloadChannels()
    {
        _channels = _channelStore.Load();
        if (_boardFilter is not ("" or "none") && _channels.All(c => c.Id != _boardFilter)) _boardFilter = "";
        _collapsedChannels.RemoveWhere(id => id != "" && _channels.All(c => c.Id != id));
    }

    // The sidebar's project list: channel headers with their projects under them.
    private void RebuildProjectRows()
    {
        ChannelRows.Apply(_projects, _channels);
        var selected = ProjectsList.SelectedItem as Project;
        _rebuildingProjectRows = true;
        try
        {
            ProjectsList.ItemsSource = ChannelRows.Build(_projects, _channels, _collapsedChannels);
            if (selected != null && ProjectsList.Items.Contains(selected)) ProjectsList.SelectedItem = selected;
        }
        finally
        {
            _rebuildingProjectRows = false;
        }
        ApplyGoalChannelColors();
    }

    private void ChannelHeader_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChannelHeader header) return;
        var id = header.ChannelId ?? "";
        if (!_collapsedChannels.Remove(id)) _collapsedChannels.Add(id);

        var settings = _settingsStore.Load();
        settings.CollapsedChannels = _collapsedChannels.ToList();
        _settingsStore.Save(settings);
        RebuildProjectRows();
    }

    private void OpenChannelsManager(bool addOne = false)
    {
        var window = new ChannelsWindow(_channelStore.Load(), addOne) { Owner = this };
        if (window.ShowDialog() != true) return;
        ReloadChannels();
        RebuildProjectRows();
        ShowBoard();
    }

    private void AssignChannel(Project project, string? channelId)
    {
        project.ChannelId = channelId;
        _projectStore.Save(_projects);
        RebuildProjectRows();
        ShowBoard();
    }

    // ---------- Right-click menu ----------

    private void BuildProjectMenu(ContextMenu menu, object? row)
    {
        menu.Items.Clear();
        if (row is ChannelHeader)
        {
            var rename = new MenuItem { Header = "Rename or recolor channels…" };
            rename.Click += (_, _) => OpenChannelsManager();
            menu.Items.Add(rename);
            return;
        }
        if (row is not Project project) return;

        var edit = new MenuItem { Header = "Edit…" };
        edit.Click += EditProject_Click;
        menu.Items.Add(edit);

        var channel = new MenuItem { Header = "Channel" };
        var none = new MenuItem { Header = "No channel", IsCheckable = true, IsChecked = project.ChannelColor == null };
        none.Click += (_, _) => AssignChannel(project, null);
        channel.Items.Add(none);
        foreach (var c in _channels)
        {
            var dot = new Ellipse { Width = 10, Height = 10, Fill = (Brush)new BrushConverter().ConvertFromString(c.Color)! };
            var item = new MenuItem { Header = c.Name, Icon = dot, IsCheckable = true, IsChecked = project.ChannelId == c.Id };
            item.Click += (_, _) => AssignChannel(project, c.Id);
            channel.Items.Add(item);
        }
        channel.Items.Add(new Separator());
        var add = new MenuItem { Header = "New channel…" };
        add.Click += (_, _) => OpenChannelsManager(addOne: true);
        channel.Items.Add(add);
        if (_channels.Count > 0)
        {
            var manage = new MenuItem { Header = "Manage channels…" };
            manage.Click += (_, _) => OpenChannelsManager();
            channel.Items.Add(manage);
        }
        menu.Items.Add(channel);

        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "Delete…" };
        delete.Click += DeleteProject_Click;
        menu.Items.Add(delete);
    }

    // ---------- Colors on tasks ----------

    private string? ChannelColorOf(DailyGoal goal) =>
        goal.ProjectId == null ? null : _projects.FirstOrDefault(p => p.Id == goal.ProjectId)?.ChannelColor;

    private void ApplyGoalChannelColors()
    {
        foreach (var goal in _dailyGoals) goal.ChannelColor = ChannelColorOf(goal);
    }

    // ---------- Home board filter ----------

    private bool InBoardFilter(Project project) => _boardFilter switch
    {
        "" => true,
        "none" => !project.HasChannel,
        var id => project.ChannelId == id
    };

    private void SetBoardFilter(string id)
    {
        _boardFilter = id;
        var settings = _settingsStore.Load();
        settings.BoardChannelFilter = id;
        _settingsStore.Save(settings);
        ShowBoard();
    }

    // "All · Main 3 · Second 2 ⚠1 · Third 1 · ＋": pick a channel to see just its projects.
    private void BuildChannelPills()
    {
        BoardChannelPills.Children.Clear();
        if (_board == null) return;

        if (_channels.Count > 0)
        {
            BoardChannelPills.Children.Add(ChannelPill("All", null, _board.Cards.Count, 0, _boardFilter == "", ""));
            foreach (var channel in _channels)
            {
                var mine = _board.Cards.Where(c => c.Project.ChannelId == channel.Id).ToList();
                BoardChannelPills.Children.Add(ChannelPill(channel.Name, channel.Color, mine.Count, mine.Count(c => c.StatusLabel == "Behind pace"),
                    _boardFilter == channel.Id, channel.Id));
            }
            var loose = _board.Cards.Where(c => !c.Project.HasChannel).ToList();
            if (loose.Count > 0)
                BoardChannelPills.Children.Add(ChannelPill("No channel", "#94A3B8", loose.Count, loose.Count(c => c.StatusLabel == "Behind pace"), _boardFilter == "none", "none"));
        }

        var add = new Button
        {
            Content = new TextBlock { Text = _channels.Count == 0 ? "＋  Group projects by channel" : "＋", FontSize = 12, Margin = new Thickness(2, 2, 2, 2) },
            Cursor = Cursors.Hand,
            Focusable = false,
            Margin = new Thickness(0, 0, 8, 8),
            ToolTip = "Add or edit channels",
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        ((TextBlock)add.Content).SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        System.Windows.Automation.AutomationProperties.SetName(add, "Edit channels");
        add.Click += (_, _) => OpenChannelsManager(addOne: _channels.Count == 0);
        BoardChannelPills.Children.Add(add);
    }

    private Button ChannelPill(string label, string? color, int count, int behind, bool selected, string filterId)
    {
        var parts = new StackPanel { Orientation = Orientation.Horizontal };
        if (color != null)
            parts.Children.Add(new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Fill = (Brush)new BrushConverter().ConvertFromString(color)! });
        var name = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, selected ? "AccentBrush" : "TextBrush");
        parts.Children.Add(name);
        var number = new TextBlock { Text = count.ToString(), FontSize = 11, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        number.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        parts.Children.Add(number);
        if (behind > 0)
        {
            var warn = new TextBlock { Text = $"{behind} behind", FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            warn.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            parts.Children.Add(warn);
        }

        var pill = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12, 4, 12, 4), Child = parts };
        pill.SetResourceReference(Border.BackgroundProperty, selected ? "CardHoverBrush" : "CardBrush");
        pill.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");

        var button = new Button
        {
            Content = pill,
            Cursor = Cursors.Hand,
            Focusable = false,
            Margin = new Thickness(0, 0, 8, 8),
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{label} channel");
        button.Click += (_, _) => SetBoardFilter(filterId);
        return button;
    }
}
