using System.Windows;
using System.Windows.Controls;
using BijouHub.Services;

namespace BijouHub.Views;

// A question with a few answers, e.g. "Delete project only" / "Delete project and its list".
// Choice is the picked answer's index, or -1 for Cancel.
public partial class ChoiceWindow : Window
{
    public int Choice { get; private set; } = -1;

    public ChoiceWindow(string title, string message, params string[] choices)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Title = title;
        MessageText.Text = message;
        for (var i = 0; i < choices.Length; i++)
        {
            var index = i;
            var button = new Button { Content = choices[i], Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
            if (i == choices.Length - 1) button.SetResourceReference(StyleProperty, "AccentButton");
            button.Click += (_, _) =>
            {
                Choice = index;
                DialogResult = true;
            };
            Choices.Children.Add(button);
        }
    }

    public static int Ask(Window owner, string title, string message, params string[] choices)
    {
        var dialog = new ChoiceWindow(title, message, choices) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Choice : -1;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
