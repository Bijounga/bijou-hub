using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Views;

// Add, rename, recolor and delete channels. Saved to the channel store on Save.
public partial class ChannelsWindow : Window
{
    private readonly List<Channel> _channels;
    private Channel? _focusNew;

    public ChannelsWindow(IEnumerable<Channel> channels, bool addOne = false)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
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
        EmptyText.Visibility = _channels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var channel in _channels) Rows.Children.Add(BuildRow(channel));
    }

    private UIElement BuildRow(Channel channel)
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var color in ChannelPalette.Colors)
        {
            var selected = string.Equals(color, channel.Color, StringComparison.OrdinalIgnoreCase);
            var dot = new Ellipse { Width = 14, Height = 14, Fill = Brush(color) };
            var ring = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 2, 0),
                BorderThickness = new Thickness(2), Child = dot, Background = Brushes.Transparent
            };
            if (selected) ring.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
            else ring.BorderBrush = Brushes.Transparent;

            var swatch = new Button
            {
                Content = ring, Cursor = System.Windows.Input.Cursors.Hand, Focusable = false, ToolTip = color,
                Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, $"Color {color}");
            swatch.Click += (_, _) =>
            {
                channel.Color = color;
                Render();
            };
            swatches.Children.Add(swatch);
        }

        var name = new TextBox { Text = channel.Name, Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center, MinWidth = 150 };
        System.Windows.Automation.AutomationProperties.SetName(name, "Channel name");
        name.TextChanged += (_, _) => channel.Name = name.Text;
        if (channel == _focusNew) name.Loaded += (_, _) => name.Focus();

        var delete = new Button
        {
            Content = "✕", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Delete channel", Focusable = false,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }
        };
        delete.Foreground = Brushes.Gray;
        delete.Cursor = System.Windows.Input.Cursors.Hand;
        System.Windows.Automation.AutomationProperties.SetName(delete, "Delete channel");
        delete.Click += (_, _) =>
        {
            _channels.Remove(channel);
            Render();
        };

        // Name and delete on one line, the colors under it.
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(delete, Dock.Right);
        line.Children.Add(delete);
        line.Children.Add(name);
        stack.Children.Add(line);
        stack.Children.Add(swatches);
        var card = new Border
        {
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 10, 10, 6), Margin = new Thickness(0, 0, 0, 8),
            Child = stack
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "SoftBorderBrush");
        return card;
    }

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        AddChannel();
        Render();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Blank names aren't channels.
        var kept = _channels.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        foreach (var channel in kept) channel.Name = channel.Name.Trim();
        new ChannelStore().Save(kept);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
