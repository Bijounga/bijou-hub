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

    // A question with a few answers ("Delete project only", "Delete project and its list"):
    // the picked answer's index, or -1 for Cancel. The last answer is the strongest.
    public static async Task<int> Choose(Window owner, string title, string message, params string[] choices)
    {
        var prompt = new PromptWindow(title, message);
        prompt.OkButton.IsVisible = false;
        for (var i = 0; i < choices.Length; i++)
        {
            var index = i;
            var button = new Button { Content = choices[i], MinWidth = 90, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            if (i == choices.Length - 1) button.Classes.Add("primary");
            button.Click += (_, _) => prompt.Close(index);
            prompt.ButtonRow.Children.Add(button);
        }
        prompt.CancelButton.Click -= prompt.Cancel_Click;
        prompt.CancelButton.Click += (_, _) => prompt.Close(-1);
        return await prompt.ShowDialog<object?>(owner) is int picked ? picked : -1;
    }

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
