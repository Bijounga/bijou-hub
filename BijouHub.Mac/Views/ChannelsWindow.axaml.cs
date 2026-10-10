using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BijouHub.Mac.Controls;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

// Add, rename, recolor and delete channels. Saved to the channel store on Save.
public partial class ChannelsWindow : Window
{
    private readonly List<Channel> _channels;
    private Channel? _focusNew;
    private readonly IReadOnlyDictionary<string, string> _hints;

    public ChannelsWindow() : this(Array.Empty<Channel>()) { }

    // `hints` (channel id → the projects in it) is for restoring channels whose names didn't reach
    // this computer: each unnamed one shows which projects it holds, so you know which is which.
    public ChannelsWindow(IEnumerable<Channel> channels, bool addOne = false, IReadOnlyDictionary<string, string>? hints = null)
    {
        InitializeComponent();
        _hints = hints ?? new Dictionary<string, string>();
        if (_hints.Count > 0)
            IntroText.Text = "These channels exist on your other computer but their names didn't arrive here. Name each one: the projects in it are listed so you can tell which is which.";
        // A working copy: Cancel leaves everything as it was.
        _channels = channels.Select(c => new Channel { Id = c.Id, Name = c.Name, Color = c.Color }).ToList();
        if (addOne) AddChannel();
        Render();
    }

    private void AddChannel()
    {
        _focusNew = new Channel { Name = "", Color = ChannelPalette.Next(_channels) };
        _channels.Add(_focusNew);
    }

    private void Render()
    {
        Rows.Children.Clear();
        EmptyText.IsVisible = _channels.Count == 0;
        foreach (var channel in _channels) Rows.Children.Add(BuildRow(channel));
    }

    private Control BuildRow(Channel channel)
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var color in ChannelPalette.Colors)
        {
            var selected = string.Equals(color, channel.Color, StringComparison.OrdinalIgnoreCase);
            var swatch = new Button
            {
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                BorderBrush = selected ? (IBrush?)this.FindResource("TextBrush") ?? Brushes.White : Brushes.Transparent,
                CornerRadius = new CornerRadius(12),
                Width = 24,
                Height = 24,
                Content = new Avalonia.Controls.Shapes.Ellipse { Width = 14, Height = 14, Fill = HexBrushConverter.Parse(color) },
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            ToolTip.SetTip(swatch, color);
            Avalonia.Automation.AutomationProperties.SetName(swatch, $"Color {color}");
            swatch.Click += (_, _) =>
            {
                channel.Color = color;
                Render();
            };
            swatches.Children.Add(swatch);
        }

        var name = new TextBox { Text = channel.Name, Watermark = "Channel name" };
        Avalonia.Automation.AutomationProperties.SetName(name, "Channel name");
        name.TextChanged += (_, _) => channel.Name = name.Text ?? "";
        if (channel == _focusNew) name.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => name.Focus());

        var delete = new Button { Content = "✕", Classes = { "ghost" }, Padding = new Thickness(8, 4) };
        ToolTip.SetTip(delete, "Delete channel");
        Avalonia.Automation.AutomationProperties.SetName(delete, "Delete channel");
        delete.Click += (_, _) =>
        {
            _channels.Remove(channel);
            Render();
        };

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        line.Children.Add(name);
        Grid.SetColumn(delete, 1);
        line.Children.Add(delete);

        var stack = new StackPanel { Spacing = 8 };
        if (_hints.TryGetValue(channel.Id, out var projects))
            stack.Children.Add(new TextBlock { Text = "Holds: " + projects, FontSize = 11, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
        stack.Children.Add(line);
        stack.Children.Add(swatches);
        return new Border
        {
            Classes = { "card" },
            Padding = new Thickness(10, 10, 10, 8),
            Child = stack
        };
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        AddChannel();
        Render();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        // Blank names aren't channels.
        var kept = _channels.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        foreach (var channel in kept) channel.Name = channel.Name.Trim();
        new ChannelStore().Save(kept);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
