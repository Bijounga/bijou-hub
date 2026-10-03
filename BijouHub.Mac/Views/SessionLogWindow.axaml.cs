using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

// Every session by day, with a project picker on each so time started without a project (e.g.
// from a Stream Deck key) can be assigned later.
public partial class SessionLogWindow : Window
{
    private readonly SessionLogService _logService;
    private readonly IReadOnlyList<Project> _projects;
    private readonly List<SessionRecord> _sessions;

    public bool AssignmentsChanged { get; private set; }

    public SessionLogWindow() : this(new SessionLogService(), new List<Project>()) { }

    public SessionLogWindow(SessionLogService logService, IReadOnlyList<Project> projects)
    {
        InitializeComponent();
        _logService = logService;
        _projects = projects;
        _sessions = logService.GetAll();
        BuildList();
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private void UnassignedOnly_Changed(object? sender, RoutedEventArgs e) => BuildList();

    private void BuildList()
    {
        UpdateUnassignedSummary();
        var unassignedOnly = UnassignedOnlyCheck.IsChecked == true;
        var visible = unassignedOnly ? _sessions.Where(s => s.ProjectId == null).ToList() : _sessions;

        var items = new List<Control>();
        foreach (var day in visible.GroupBy(s => s.StartTime.Date).OrderByDescending(g => g.Key))
        {
            var sessions = day.ToList();
            var holder = new ContentControl();
            holder.AttachedToVisualTree += (_, _) => holder.Content ??= BuildDay(sessions);
            items.Add(holder);
        }
        if (visible.Count == 0)
        {
            items.Add(new TextBlock
            {
                Text = unassignedOnly ? "Every session is assigned to a project." : "No sessions logged yet. Finish a session to see it here.",
                Foreground = Brush("MutedTextBrush")
            });
        }
        LogItems.ItemsSource = items;
    }

    private StackPanel BuildDay(List<SessionRecord> day)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        var total = TimeSpan.FromSeconds(day.Sum(s => s.ActiveSeconds));
        panel.Children.Add(new TextBlock
        {
            Text = $"{day[0].StartTime:dddd, MMM d}   —   {FormatSpan(total)} total",
            FontWeight = FontWeight.Bold,
            Foreground = Brush("AccentBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(new TextBlock
        {
            Text = string.Join("   •   ", day.GroupBy(s => s.ModeName).Select(g => $"{(g.Key.Length > 0 ? g.Key : "No mode")}: {FormatSpan(TimeSpan.FromSeconds(g.Sum(s => s.ActiveSeconds)))}")),
            Foreground = Brush("MutedTextBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var session in day.OrderByDescending(s => s.StartTime)) panel.Children.Add(BuildRow(session));
        return panel;
    }

    private Grid BuildRow(SessionRecord session)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,190"), Margin = new Thickness(0, 0, 0, 4) };
        var idle = session.IdleSeconds > 0 ? $"  (idle {FormatSpan(TimeSpan.FromSeconds(session.IdleSeconds))})" : "";
        row.Children.Add(new TextBlock
        {
            Text = $"  {session.StartTime:HH:mm} - {session.EndTime:HH:mm}   {session.ModeName}   {FormatSpan(TimeSpan.FromSeconds(session.ActiveSeconds))}{idle}",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var picker = BuildPicker(session);
        Grid.SetColumn(picker, 1);
        row.Children.Add(picker);
        return row;
    }

    // Starts with only the current choice; the project list fills in when the dropdown first opens.
    private ComboBox BuildPicker(SessionRecord session)
    {
        var picker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 3) };
        ToolTip.SetTip(picker, "Assign this time to a project");
        var none = new ComboBoxItem { Content = "— No project —" };
        var currentProject = session.ProjectId == null ? null : _projects.FirstOrDefault(p => p.Id == session.ProjectId);
        var current = session.ProjectId == null
            ? none
            : currentProject != null
                ? new ComboBoxItem { Content = currentProject.Name, Tag = currentProject }
                : new ComboBoxItem { Content = $"{session.ProjectName ?? "Project"} (deleted)" };
        picker.Items.Add(current);
        picker.SelectedItem = current;

        var filled = false;
        picker.DropDownOpened += (_, _) =>
        {
            if (filled) return;
            filled = true;
            var selected = picker.SelectedItem;
            picker.Items.Clear();
            picker.Items.Add(none);
            foreach (var project in _projects)
                picker.Items.Add(currentProject == project ? selected : new ComboBoxItem { Content = project.Name, Tag = project });
            if (selected != none && currentProject == null) picker.Items.Add(selected);
            picker.SelectedItem = selected;
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (!filled) return;
            if (picker.SelectedItem == none) Assign(session, null, null);
            else if (picker.SelectedItem is ComboBoxItem { Tag: Project project }) Assign(session, project.Id, project.Name);
        };
        return picker;
    }

    private void Assign(SessionRecord session, string? projectId, string? projectName)
    {
        if (session.ProjectId == projectId) return;
        _logService.AssignProject(session.Id, projectId, projectName);
        session.ProjectId = projectId;
        session.ProjectName = projectName;
        AssignmentsChanged = true;
        UpdateUnassignedSummary();
    }

    private void UpdateUnassignedSummary()
    {
        var unassigned = _sessions.Where(s => s.ProjectId == null).ToList();
        UnassignedSummary.Text = unassigned.Count == 0
            ? "All time is assigned to projects."
            : $"{unassigned.Count} session{(unassigned.Count == 1 ? "" : "s")} not on a project — {FormatSpan(TimeSpan.FromSeconds(unassigned.Sum(s => s.ActiveSeconds)))}. Pick a project to assign the time.";
    }

    private static string FormatSpan(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m {span.Seconds}s";
}
