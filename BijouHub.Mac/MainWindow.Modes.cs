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
        if (_shownMode is WorkMode mode && DurationText.TryParseMinutes(ModeTimerBox.Text) is int minutes)
            await BeginSession(mode, null, null, minutes, countDown: true);
    }

    private async void ModeTimerStart_Click(object? sender, RoutedEventArgs e)
    {
        if (_shownMode is WorkMode mode && DurationText.TryParseMinutes(ModeTimerBox.Text) is int minutes)
            await BeginSession(mode, null, null, minutes, countDown: true);
    }

    private void ModeTimerSave_Click(object? sender, RoutedEventArgs e)
    {
        if (_shownMode is not WorkMode mode || DurationText.TryParseMinutes(ModeTimerBox.Text) is not int minutes) return;
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
        var saved = minutes is int m && _shownMode?.TimerMinutes.Contains(m) == true;
        ModeTimerStartButton.IsEnabled = minutes != null;
        ModeTimerSaveButton.IsEnabled = minutes != null && !saved;

        if (string.IsNullOrWhiteSpace(text))
        {
            ModeTimerHint.IsVisible = false;
            return;
        }
        ModeTimerHint.IsVisible = true;
        ModeTimerHint.Text = minutes is int valid ? $"Counts down from {DurationText.Format(valid)}." : "Can't read that — try 45, 1:30 or 2h.";
        ModeTimerHint.Foreground = Brush(minutes != null ? "MutedTextBrush" : "DangerBrush");
    }
}
