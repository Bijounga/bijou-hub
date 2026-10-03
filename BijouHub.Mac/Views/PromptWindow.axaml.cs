using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BijouHub.Mac.Views;

// Small modal for questions (Delete?), one-line answers (a list name) and notices.
public partial class PromptWindow : Window
{
    public string Value => ValueBox.Text ?? "";

    public PromptWindow() : this("BijouHub", "") { }

    public PromptWindow(string title, string message, string ok = "OK", string? cancel = "Cancel", bool askForText = false, string? initial = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        OkButton.Content = ok;
        CancelButton.IsVisible = cancel != null;
        if (cancel != null) CancelButton.Content = cancel;
        ValueBox.IsVisible = askForText;
        ValueBox.Text = initial;
        Opened += (_, _) => (askForText ? (Control)ValueBox : OkButton).Focus();
    }

    public static Task<bool> Confirm(Window owner, string title, string message, string ok = "OK") =>
        new PromptWindow(title, message, ok).ShowDialog<bool>(owner);

    public static Task Notice(Window owner, string title, string message) =>
        new PromptWindow(title, message, cancel: null).ShowDialog<bool>(owner);

    public static async Task<string?> Ask(Window owner, string title, string message, string? initial = null)
    {
        var prompt = new PromptWindow(title, message, askForText: true, initial: initial);
        return await prompt.ShowDialog<bool>(owner) && !string.IsNullOrWhiteSpace(prompt.Value) ? prompt.Value.Trim() : null;
    }

    private void ValueBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Close(true);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
