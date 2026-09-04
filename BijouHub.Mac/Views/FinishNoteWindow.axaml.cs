using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BijouHub.Mac.Views;

public partial class FinishNoteWindow : Window
{
    public string? Note { get; private set; }

    public FinishNoteWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var did = DidBox.Text?.Trim() ?? "";
        var next = NextBox.Text?.Trim() ?? "";

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(did)) parts.Add($"Did: {did}");
        if (!string.IsNullOrEmpty(next)) parts.Add($"Next: {next}");

        Note = parts.Count > 0 ? string.Join("  |  ", parts) : null;
        Close(true);
    }

    private void Skip_Click(object? sender, RoutedEventArgs e)
    {
        Note = null;
        Close(true);
    }
}
