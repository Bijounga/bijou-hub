using System.Windows;
using BijouHub.Services;

namespace BijouHub.Views;

public partial class LogTimeWindow : Window
{
    public int TotalMinutes { get; private set; }
    public string? Note { get; private set; }

    public LogTimeWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        int.TryParse(HoursBox.Text.Trim(), out var hours);
        int.TryParse(MinutesBox.Text.Trim(), out var minutes);

        TotalMinutes = Math.Max(0, hours) * 60 + Math.Max(0, minutes);
        if (TotalMinutes <= 0)
        {
            MessageBox.Show("Enter a duration greater than zero.", "Log Time",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var note = NoteBox.Text.Trim();
        Note = string.IsNullOrEmpty(note) ? null : note;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
