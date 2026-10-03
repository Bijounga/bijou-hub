using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using BijouHub.Mac.Controls;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// Modes: what to open for a kind of work, plus saved countdown timers. (App blocking is
// Windows-only, so a Mac mode just launches.)
public partial class MainWindow
{
    private void ShowMode(WorkMode mode)
    {
        ShowOnly(ModePanel);
        _shownMode = mode;
        _detailProject = null;
        ModeNameText.Text = mode.Name;
        ModeLaunchItems.ItemsSource = mode.LaunchItems.ToList();
        ModeNoItemsText.IsVisible = mode.LaunchItems.Count == 0;
        RefreshModeTimers(mode);
        ModeTimerBox.Text = "";
        UpdateModeTimerInput();
    }

    private void PersistModes()
    {
        _modeStore.Save(_modes.ToList());
        var selected = ModesList.SelectedItem;
        ModesList.ItemsSource = null;
        ModesList.ItemsSource = _modes;
        ModesList.SelectedItem = selected;
    }

    private async void NewMode_Click(object? sender, RoutedEventArgs e)
    {
        var mode = new WorkMode();
        if (!await new ModeEditorWindow(mode).ShowDialog<bool>(this)) return;
        _modes.Add(mode);
        PersistModes();
        ProjectsList.SelectedItem = null;
        ModesList.SelectedItem = mode;
    }

    private async void EditMode_Click(object? sender, RoutedEventArgs e)
    {
        if (_shownMode is not WorkMode mode) return;
        if (!await new ModeEditorWindow(mode).ShowDialog<bool>(this)) return;
        PersistModes();
        ShowMode(mode);
    }

    private async void DeleteMode_Click(object? sender, RoutedEventArgs e)
    {
        if (_shownMode is not WorkMode mode) return;
        if (!await PromptWindow.Confirm(this, "Delete mode", $"Delete the mode \"{mode.Name}\"?", "Delete")) return;
        _modes.Remove(mode);
        PersistModes();
        ModesList.SelectedItem = null;
        ShowHome();
    }

    private async void Launch_Click(object? sender, RoutedEventArgs e)
    {
        if (_shownMode is WorkMode mode) await BeginSession(mode, null, null, null);
    }

    // ---------- Timers ----------

    private void RefreshModeTimers(WorkMode mode)
    {
        ModeTimerTiles.Children.Clear();
        foreach (var minutes in mode.TimerMinutes.Order())
            ModeTimerTiles.Children.Add(BuildTimerTile(mode, minutes));
        foreach (var plan in mode.PomodoroTimers.Select(DurationText.TryParsePomodoro).OfType<PomodoroPlan>())
            ModeTimerTiles.Children.Add(BuildPomodoroTile(mode, plan));
    }

    // A saved "25/5": focus and break lengths on one tile.
    private Button BuildPomodoroTile(WorkMode mode, PomodoroPlan plan)
    {
        var tile = new Button
        {
            Width = 96,
            Height = 72,
            Margin = new Thickness(0, 0, 12, 12),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = plan.Rounds is int r ? $"POMODORO ×{r}" : "POMODORO", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("AccentBrush") },
                    new TextBlock { Text = $"{plan.FocusMinutes}/{plan.BreakMinutes}", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = (Avalonia.Media.FontFamily?)(this.TryFindResource("TimerFont", out var f) ? f : null) ?? Avalonia.Media.FontFamily.Default }
                }
            }
        };
        ToolTip.SetTip(tile, $"Launch {mode.Name}: {plan.FocusMinutes} min focus, {plan.BreakMinutes} min break, " + (plan.Rounds is int n ? $"{n} rounds" : "repeating"));
        Avalonia.Automation.AutomationProperties.SetName(tile, $"Pomodoro {plan.FocusMinutes} {plan.BreakMinutes}" + (plan.Rounds is int count ? $" x{count}" : ""));
        tile.Click += async (_, _) => await BeginSession(mode, null, null, plan.FocusMinutes, pomodoro: plan);

        var remove = new MenuItem { Header = "Remove timer" };
        remove.Click += (_, _) =>
        {
            mode.PomodoroTimers.Remove(plan.ToString());
            _modeStore.Save(_modes.ToList());
            RefreshModeTimers(mode);
            UpdateModeTimerInput();
        };
        tile.ContextMenu = new ContextMenu { Items = { remove } };
        return tile;
    }

    // The timer box takes a length ("45") or a Pomodoro ("25/5").
    private async Task StartFromTimerBox()
    {
        if (_shownMode is not WorkMode mode) return;
        if (DurationText.TryParsePomodoro(ModeTimerBox.Text) is PomodoroPlan plan)
            await BeginSession(mode, null, null, plan.FocusMinutes, pomodoro: plan);
        else if (DurationText.TryParseMinutes(ModeTimerBox.Text) is int minutes)
            await BeginSession(mode, null, null, minutes, countDown: true);
    }

    private Button BuildTimerTile(WorkMode mode, int minutes)
    {
        var label = DurationText.Format(minutes);
        var tile = new Button
        {
            Width = 96,
            Height = 72,
            Margin = new Thickness(0, 0, 12, 12),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new LineIcon { Kind = "play", Width = 11, Height = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("AccentBrush") },
                    new TextBlock { Text = label, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = (Avalonia.Media.FontFamily?)(this.TryFindResource("TimerFont", out var f) ? f : null) ?? Avalonia.Media.FontFamily.Default }
                }
            }
        };
        ToolTip.SetTip(tile, $"Launch {mode.Name} and count down from {label}");
        tile.Click += async (_, _) => await BeginSession(mode, null, null, minutes, countDown: true);

        var remove = new MenuItem { Header = "Remove timer" };
        remove.Click += (_, _) =>
        {
            mode.TimerMinutes.Remove(minutes);
            _modeStore.Save(_modes.ToList());
            RefreshModeTimers(mode);
            UpdateModeTimerInput();
        };
        tile.ContextMenu = new ContextMenu { Items = { remove } };
        return tile;
    }

    private void ModeTimerBox_TextChanged(object? sender, TextChangedEventArgs e) => UpdateModeTimerInput();

    private async void ModeTimerBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await StartFromTimerBox();
    }

    private async void ModeTimerStart_Click(object? sender, RoutedEventArgs e) => await StartFromTimerBox();

    private void ModeTimerSave_Click(object? sender, RoutedEventArgs e)
    {
        if (_shownMode is not WorkMode mode) return;
        if (DurationText.TryParsePomodoro(ModeTimerBox.Text) is PomodoroPlan plan)
        {
            if (mode.PomodoroTimers.Contains(plan.ToString())) return;
            mode.PomodoroTimers.Add(plan.ToString());
            _modeStore.Save(_modes.ToList());
            RefreshModeTimers(mode);
            ModeTimerBox.Text = "";
            return;
        }
        if (DurationText.TryParseMinutes(ModeTimerBox.Text) is not int minutes) return;
        if (mode.TimerMinutes.Contains(minutes)) return;
        mode.TimerMinutes.Add(minutes);
        _modeStore.Save(_modes.ToList());
        RefreshModeTimers(mode);
        ModeTimerBox.Text = "";
    }

    private void UpdateModeTimerInput()
    {
        var text = ModeTimerBox.Text ?? "";
        var minutes = DurationText.TryParseMinutes(text);
        var pomodoro = DurationText.TryParsePomodoro(text);
        var saved = pomodoro != null ? _shownMode?.PomodoroTimers.Contains(pomodoro.ToString()) == true
            : minutes is int m && _shownMode?.TimerMinutes.Contains(m) == true;
        var valid = minutes != null || pomodoro != null;
        ModeTimerStartButton.IsEnabled = valid;
        ModeTimerSaveButton.IsEnabled = valid && !saved;

        if (string.IsNullOrWhiteSpace(text))
        {
            ModeTimerHint.IsVisible = false;
            return;
        }
        ModeTimerHint.IsVisible = true;
        ModeTimerHint.Text = pomodoro != null ? $"{pomodoro.FocusMinutes} min focus, {pomodoro.BreakMinutes} min break, " + (pomodoro.Rounds is int rounds ? $"{rounds} rounds." : "on repeat. Add x4 for 4 rounds.")
            : minutes is int length ? $"Counts down from {DurationText.Format(length)}."
            : "Can't read that — try 45, 1:30, 2h, or 25/5 (25/5x4) for a Pomodoro.";
        ModeTimerHint.Foreground = Brush(valid ? "MutedTextBrush" : "DangerBrush");
    }
}
