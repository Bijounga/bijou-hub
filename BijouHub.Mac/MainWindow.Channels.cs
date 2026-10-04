using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BijouHub.Mac.Controls;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// Channels (YouTube channels, or any buckets): the sidebar groups projects under them, the Home
// board filters by them, and a color follows each project into its cards and tasks. Same
// behavior as Windows.
public partial class MainWindow
{
    private ChannelStore _channelStore = new();
    private List<Channel> _channels = new();
    private HashSet<string> _collapsedChannels = new();
    private string _boardFilter = "";
    private bool _rebuildingProjectRows;
    private object? _sidebarMenuRow;

    private void InitChannels()
    {
        var settings = _settingsStore.Load();
        _collapsedChannels = settings.CollapsedChannels.ToHashSet();
        _boardFilter = settings.BoardChannelFilter ?? "";
        _channels = _channelStore.Load();
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
            ProjectsList.ItemsSource = ChannelRows.Build(_projects.ToList(), _channels, _collapsedChannels);
            if (selected != null && ProjectsList.Items.Contains(selected)) ProjectsList.SelectedItem = selected;
        }
        finally
        {
            _rebuildingProjectRows = false;
        }
        ApplyGoalChannelColors();
    }

    // Channel headers aren't selectable projects: they get a plain section-title look.
    private void ProjectsList_ContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        e.Container.Classes.Set("channelhdr", e.Container is ListBoxItem { DataContext: ChannelHeader });
    }

    private void ChannelHeader_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ChannelHeader header) return;
        var id = header.ChannelId ?? "";
        if (!_collapsedChannels.Remove(id)) _collapsedChannels.Add(id);

        var settings = _settingsStore.Load();
        settings.CollapsedChannels = _collapsedChannels.ToList();
        _settingsStore.Save(settings);
        RebuildProjectRows();
    }

    private async Task OpenChannelsManagerAsync(bool addOne = false)
    {
        var window = new ChannelsWindow(_channelStore.Load(), addOne);
        if (!await window.ShowDialog<bool>(this)) return;
        ReloadChannels();
        RebuildProjectRows();
        ShowBoard();
    }

    private void SetMainProject(Project project, bool on)
    {
        ChannelRows.SetMain(_projects, project, on);
        _projectStore.Save(_projects.ToList());
        RebuildProjectRows();
        ShowBoard();
    }

    private void AssignChannel(Project project, string? channelId)
    {
        project.ChannelId = channelId;
        // The new channel may already have a main project; the one that moved defers to it.
        if (project.IsMain && _projects.Any(p => p != project && p.IsMain && (p.ChannelId ?? "") == (channelId ?? ""))) project.IsMain = false;
        _projectStore.Save(_projects.ToList());
        RebuildProjectRows();
        ShowBoard();
    }

    // ---------- Right-click menu ----------

    private void ProjectsMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_sidebarMenuOnItem || sender is not ContextMenu menu)
        {
            e.Cancel = true;
            return;
        }

        var items = new List<object>();
        if (_sidebarMenuRow is ChannelHeader)
        {
            var rename = new MenuItem { Header = "Rename or recolor channels…" };
            rename.Click += async (_, _) => await OpenChannelsManagerAsync();
            items.Add(rename);
        }
        else if (_sidebarMenuRow is Project project)
        {
            var edit = new MenuItem { Header = "Edit…" };
            edit.Click += EditProject_Click;
            items.Add(edit);

            var main = new MenuItem { Header = project.IsMain ? "Remove main project mark" : "Mark as main project" };
            main.Click += (_, _) => SetMainProject(project, !project.IsMain);
            items.Add(main);

            var channel = new MenuItem { Header = "Channel" };
            var sub = new List<object>();
            var none = new MenuItem { Header = (project.ChannelColor == null ? "✓  " : "     ") + "No channel" };
            none.Click += (_, _) => AssignChannel(project, null);
            sub.Add(none);
            foreach (var c in _channels)
            {
                var item = new MenuItem
                {
                    Header = (project.ChannelId == c.Id ? "✓  " : "     ") + c.Name,
                    Icon = new Ellipse { Width = 10, Height = 10, Fill = HexBrushConverter.Parse(c.Color) }
                };
                item.Click += (_, _) => AssignChannel(project, c.Id);
                sub.Add(item);
            }
            sub.Add(new Separator());
            var add = new MenuItem { Header = "New channel…" };
            add.Click += async (_, _) => await OpenChannelsManagerAsync(addOne: true);
            sub.Add(add);
            if (_channels.Count > 0)
            {
                var manage = new MenuItem { Header = "Manage channels…" };
                manage.Click += async (_, _) => await OpenChannelsManagerAsync();
                sub.Add(manage);
            }
            channel.ItemsSource = sub;
            items.Add(channel);

            items.Add(new Separator());
            var delete = new MenuItem { Header = "Delete…" };
            delete.Click += DeleteProject_Click;
            items.Add(delete);
        }
        menu.ItemsSource = items;
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

    // "All · Main 3 · Second 2 · Third 1 · ＋": pick a channel to see just its projects.
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
            Content = new TextBlock { Text = _channels.Count == 0 ? "＋  Group projects by channel" : "＋", FontSize = 12 },
            Classes = { "ghost" },
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(8, 3)
        };
        ToolTip.SetTip(add, "Add or edit channels");
        Avalonia.Automation.AutomationProperties.SetName(add, "Edit channels");
        add.Click += async (_, _) => await OpenChannelsManagerAsync(addOne: _channels.Count == 0);
        BoardChannelPills.Children.Add(add);
    }

    private Button ChannelPill(string label, string? color, int count, int behind, bool selected, string filterId)
    {
        var parts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        if (color != null) parts.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = HexBrushConverter.Parse(color), VerticalAlignment = VerticalAlignment.Center });
        parts.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(selected ? "AccentBrush" : "TextBrush") });
        parts.Children.Add(new TextBlock { Text = count.ToString(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("MutedTextBrush") });
        if (behind > 0)
            parts.Children.Add(new TextBlock { Text = $"{behind} behind", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("DangerBrush") });

        var button = new Button
        {
            Content = parts,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(12, 4),
            CornerRadius = new CornerRadius(14),
            Background = Brush(selected ? "CardHoverBrush" : "CardBrush"),
            BorderBrush = Brush(selected ? "AccentBrush" : "BorderBrush"),
            BorderThickness = new Thickness(1)
        };
        Avalonia.Automation.AutomationProperties.SetName(button, $"{label} channel");
        button.Click += (_, _) => SetBoardFilter(filterId);
        return button;
    }
}
