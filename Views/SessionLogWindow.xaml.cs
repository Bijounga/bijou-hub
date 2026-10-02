using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Views;

public partial class SessionLogWindow : Window
{
    private readonly SessionLogService _logService;
    private readonly IReadOnlyList<Project> _projects;
    private List<SessionRecord> _sessions;

    // True once any session was moved to/from a project, so the caller can refresh totals.
    public bool AssignmentsChanged { get; private set; }

    public SessionLogWindow(SessionLogService logService, IReadOnlyList<Project> projects)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _logService = logService;
        _projects = projects;
        _sessions = logService.GetAll();
        BuildList();
    }

    private void UnassignedOnly_Changed(object sender, RoutedEventArgs e) => BuildList();

    private void BuildList()
    {
        LogItems.Items.Clear();
        UpdateUnassignedSummary();

        var unassignedOnly = UnassignedOnlyCheck.IsChecked == true;
        var visible = unassignedOnly ? _sessions.Where(s => s.ProjectId == null).ToList() : _sessions;
        var byDay = visible.GroupBy(s => s.StartTime.Date).OrderByDescending(g => g.Key);

        foreach (var day in byDay)
        {
            // Each day is a placeholder until it scrolls into view; the list is virtualized, so a
            // long history only builds the days on screen.
            var dayItems = day.ToList();
            var holder = new ContentControl();
            holder.Loaded += (_, _) => holder.Content ??= BuildDay(dayItems);
            LogItems.Items.Add(holder);
        }

        if (visible.Count == 0)
        {
            LogItems.Items.Add(new TextBlock
            {
                Text = unassignedOnly
                    ? "Every session is assigned to a project."
                    : "No sessions logged yet. Finish a session to see it here.",
                Foreground = (Brush)Application.Current.Resources["MutedTextBrush"]
            });
        }
    }

    private StackPanel BuildDay(List<SessionRecord> day)
    {
        var date = day[0].StartTime.Date;
        var dayPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };

        var totalActive = TimeSpan.FromSeconds(day.Sum(s => s.ActiveSeconds));
        dayPanel.Children.Add(new TextBlock
        {
            Text = $"{date:dddd, MMM d}   —   {FormatSpan(totalActive)} total",
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
            Margin = new Thickness(0, 0, 0, 6)
        });

        var byMode = day.GroupBy(s => s.ModeName)
            .Select(g => $"{(g.Key.Length > 0 ? g.Key : "No mode")}: {FormatSpan(TimeSpan.FromSeconds(g.Sum(s => s.ActiveSeconds)))}");
        dayPanel.Children.Add(new TextBlock
        {
            Text = string.Join("   •   ", byMode),
            Foreground = (Brush)Application.Current.Resources["MutedTextBrush"],
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var s in day.OrderByDescending(s => s.StartTime))
            dayPanel.Children.Add(BuildSessionRow(s));

        return dayPanel;
    }

    private Grid BuildSessionRow(SessionRecord session)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });

        var idleNote = session.IdleSeconds > 0 ? $"  (idle {FormatSpan(TimeSpan.FromSeconds(session.IdleSeconds))})" : "";
        var label = new TextBlock
        {
            Text = $"  {session.StartTime:HH:mm} - {session.EndTime:HH:mm}   {session.ModeName}   {FormatSpan(TimeSpan.FromSeconds(session.ActiveSeconds))}{idleNote}",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        row.Children.Add(label);

        var picker = BuildProjectPicker(session);
        Grid.SetColumn(picker, 1);
        row.Children.Add(picker);
        return row;
    }

    // Each row starts with only its current choice; the full project list is filled in the first
    // time that row's dropdown opens. A long history then opens instantly instead of building a
    // project list per session up front.
    private ComboBox BuildProjectPicker(SessionRecord session)
    {
        var picker = new ComboBox { Padding = new Thickness(8, 3, 8, 3), ToolTip = "Assign this time to a project" };
        var none = new ComboBoxItem { Content = "— No project —" };

        var currentProject = session.ProjectId == null ? null : _projects.FirstOrDefault(p => p.Id == session.ProjectId);
        var current = session.ProjectId == null
            ? none
            : currentProject != null
                ? new ComboBoxItem { Content = currentProject.Name, Tag = currentProject }
                // A project deleted since keeps its name on the record — show it rather than "none".
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
            if (!filled) return; // only the fill above changes selection before the list opens
            if (picker.SelectedItem == none)
                Assign(session, null, null);
            else if (picker.SelectedItem is ComboBoxItem { Tag: Project project })
                Assign(session, project.Id, project.Name);
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
            : $"{unassigned.Count} session{(unassigned.Count == 1 ? "" : "s")} not on a project — " +
              $"{FormatSpan(TimeSpan.FromSeconds(unassigned.Sum(s => s.ActiveSeconds)))}. Pick a project to assign the time.";
    }

    private static string FormatSpan(TimeSpan span)
    {
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m {span.Seconds}s";
    }
}
