using System.Windows;
using System.Windows.Input;
using BijouHub.Services;

namespace BijouHub.Views;

// One-line text question, e.g. the name for a new task list.
public partial class TextPromptWindow : Window
{
    public string Value => ValueBox.Text;

    public TextPromptWindow(string title, string prompt)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Title = title;
        PromptText.Text = prompt;
        Loaded += (_, _) => ValueBox.Focus();
    }

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Ok_Click(sender, e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = !string.IsNullOrWhiteSpace(ValueBox.Text);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
