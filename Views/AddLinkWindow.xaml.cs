using System.Windows;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Views;

public partial class AddLinkWindow : Window
{
    public LaunchItem? Result { get; private set; }

    public AddLinkWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        BrowserCombo.Items.Add("Default Browser");
        foreach (var browser in BrowserProfileService.KnownBrowserExePaths.Keys)
            BrowserCombo.Items.Add(browser);
        BrowserCombo.SelectedIndex = 0;
    }

    private void BrowserCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ProfileCombo.Items.Clear();
        var browser = BrowserCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(browser) || browser == "Default Browser")
        {
            ProfileCombo.IsEnabled = false;
            return;
        }

        ProfileCombo.IsEnabled = true;
        ProfileCombo.Items.Add(new BrowserProfile { DirectoryName = "", Label = "(default profile)" });
        foreach (var profile in BrowserProfileService.GetProfiles(browser))
            ProfileCombo.Items.Add(profile);

        ProfileCombo.DisplayMemberPath = "Label";
        ProfileCombo.SelectedIndex = 0;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("Enter a URL.", "Add Link", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;

        var browser = BrowserCombo.SelectedItem as string;
        var isDefault = string.IsNullOrEmpty(browser) || browser == "Default Browser";
        var profile = ProfileCombo.SelectedItem as BrowserProfile;

        Result = isDefault
            ? new LaunchItem { Type = LaunchItemType.Url, Path = url }
            : new LaunchItem
            {
                Type = LaunchItemType.BrowserUrl,
                Path = url,
                Browser = browser,
                BrowserProfileDir = profile?.DirectoryName,
                BrowserProfileLabel = profile?.Label
            };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
