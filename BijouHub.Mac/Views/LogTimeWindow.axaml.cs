using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BijouHub.Mac.Views;

public partial class LogTimeWindow : Window
{
    public int TotalMinutes { get; private set; }
    public string? Note { get; private set; }

    public LogTimeWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        int.TryParse(HoursBox.Text?.Trim(), out var hours);
        int.TryParse(MinutesBox.Text?.Trim(), out var minutes);

        TotalMinutes = Math.Max(0, hours) * 60 + Math.Max(0, minutes);
        if (TotalMinutes <= 0) return;

        var note = NoteBox.Text?.Trim();
        Note = string.IsNullOrEmpty(note) ? null : note;

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
