using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BijouHub.Services;

namespace BijouHub.Views;

// The date-and-time picker behind a goal's calendar icon: quick picks (Today, Tomorrow, Next
// week), a month to click a day in, and a time to type or pick. Every change is reported at
// once, so there's no Save step; clicking away closes it.
public partial class DuePicker : UserControl
{
    // The day (null: no date) and time (null: no time) after each change.
    public event Action<DateTime?, TimeOnly?>? Picked;

    private DateTime? _date;
    private TimeOnly? _time;
    private DateTime _month;

    public DuePicker()
    {
        InitializeComponent();

        var culture = CultureInfo.CurrentCulture.DateTimeFormat;
        for (var i = 0; i < 7; i++)
        {
            var name = culture.ShortestDayNames[((int)culture.FirstDayOfWeek + i) % 7];
            var label = new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            DayNames.Children.Add(label);
        }

        AddQuickPick("Today", () => DateTime.Today);
        AddQuickPick("Tomorrow", () => DateTime.Today.AddDays(1));
        AddQuickPick("Next week", () => NextMonday(DateTime.Today));
        AddQuickPick("No date", () => null);

        for (var minutes = 0; minutes < 24 * 60; minutes += 30)
        {
            var time = new TimeOnly(minutes / 60, minutes % 60);
            TimeList.Items.Add(new ListBoxItem { Content = DueText.Time12(time), Tag = time });
        }
    }

    public void Load(DateTime? date, TimeOnly? time)
    {
        _date = date;
        _time = time;
        _month = new DateTime((date ?? DateTime.Today).Year, (date ?? DateTime.Today).Month, 1);
        TimeBox.Text = time is TimeOnly t ? DueText.Time12(t) : "";
        TimeList.Visibility = Visibility.Collapsed;
        RenderMonth();
        ClearTimeButton.Visibility = _time != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static DateTime NextMonday(DateTime today)
    {
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(days == 0 ? 7 : days);
    }

    private void AddQuickPick(string label, Func<DateTime?> day)
    {
        var chip = new Button { Content = label, Style = (Style)Resources["PickChip"] };
        chip.Click += (_, _) =>
        {
            _date = day();
            if (_date == null) _time = null; // no date, no reminder time either
            if (_date is DateTime d) _month = new DateTime(d.Year, d.Month, 1);
            if (_time == null) TimeBox.Text = "";
            Raise();
        };
        QuickPicks.Children.Add(chip);
    }

    private void RenderMonth()
    {
        MonthText.Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        DayGrid.Children.Clear();

        var today = DateTime.Today;
        var offset = ((int)_month.DayOfWeek - (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        var first = _month.AddDays(-offset);
        PrevMonth.IsEnabled = _month > new DateTime(today.Year, today.Month, 1);

        for (var i = 0; i < 42; i++)
        {
            var day = first.AddDays(i);
            var button = new Button
            {
                Content = day.Day.ToString(CultureInfo.CurrentCulture),
                Style = (Style)Resources["DayButton"],
                IsEnabled = day >= today,
                ToolTip = day.ToString("dddd, MMMM d", CultureInfo.CurrentCulture)
            };
            System.Windows.Automation.AutomationProperties.SetName(button, day.ToString("D", CultureInfo.CurrentCulture));

            var selected = _date?.Date == day;
            if (selected)
            {
                button.SetResourceReference(BackgroundProperty, "AccentBrush");
                button.SetResourceReference(ForegroundProperty, "BgBrush");
            }
            else
            {
                button.SetResourceReference(ForegroundProperty, day.Month == _month.Month ? "TextBrush" : "FaintTextBrush");
            }
            if (day == today && !selected) button.SetResourceReference(BorderBrushProperty, "AccentBrush");

            button.Click += (_, _) =>
            {
                _date = day;
                Raise();
            };
            DayGrid.Children.Add(button);
        }
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        RenderMonth();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(1);
        RenderMonth();
    }

    // ---------- Time ----------

    private void TimeBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        TimeList.Visibility = Visibility.Visible;
        // Start the list at the chosen time, or around now.
        var anchor = _time ?? TimeOnly.FromDateTime(DateTime.Now);
        var index = Math.Clamp(anchor.Hour * 2 + (anchor.Minute >= 30 ? 1 : 0), 0, TimeList.Items.Count - 1);
        TimeList.SelectedIndex = -1;
        TimeList.UpdateLayout();
        TimeList.ScrollIntoView(TimeList.Items[Math.Min(index + 3, TimeList.Items.Count - 1)]);
        TimeList.ScrollIntoView(TimeList.Items[index]);
    }

    private void TimeBox_TextChanged(object sender, TextChangedEventArgs e) =>
        TimePlaceholder.Visibility = TimeBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void TimeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        CommitTypedTime();
        TimeList.Visibility = Visibility.Collapsed;
    }

    private void TimeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is DependencyObject next && IsInside(next, TimeList)) return;
        CommitTypedTime();
    }

    private void CommitTypedTime()
    {
        var text = TimeBox.Text.Trim();
        var parsed = DueText.ParseTime(text);
        if (text.Length == 0) _time = null;
        else if (parsed != null) _time = parsed;
        TimeBox.Text = _time is TimeOnly t ? DueText.Time12(t) : "";
        TimeList.Visibility = Visibility.Collapsed;
        Raise();
    }

    private void TimeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TimeList.SelectedItem is not ListBoxItem { Tag: TimeOnly time }) return;
        _time = time;
        TimeBox.Text = DueText.Time12(time);
        TimeList.Visibility = Visibility.Collapsed;
        Raise();
    }

    private void ClearTime_Click(object sender, RoutedEventArgs e)
    {
        _time = null;
        TimeBox.Text = "";
        TimeList.Visibility = Visibility.Collapsed;
        Raise();
    }

    // A time with no day means today.
    private void Raise()
    {
        if (_time != null && _date == null) _date = DateTime.Today;
        ClearTimeButton.Visibility = _time != null ? Visibility.Visible : Visibility.Collapsed;
        RenderMonth();
        Picked?.Invoke(_date, _time);
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current == container) return true;
        return false;
    }
}
