using Avalonia.Controls;
using Avalonia.Interactivity;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

// Where the Elgato Key Light is, and whether it goes off again when a session ends. Saved to the
// app settings on Save.
public partial class KeyLightWindow : Window
{
    private readonly AppSettingsStore _store = new();

    public KeyLightWindow()
    {
        InitializeComponent();
        var settings = _store.Load();
        AddressBox.Text = settings.KeyLightAddress ?? "";
        OffBox.IsChecked = settings.KeyLightOffWhenDone;
    }

    private async void Find_Click(object? sender, RoutedEventArgs e)
    {
        FindButton.IsEnabled = false;
        StatusText.Text = "Looking on your network…";
        try
        {
            var found = await KeyLightService.ScanAsync();
            if (found.Count == 0)
            {
                StatusText.Text = "Didn't find one. Check the light is on and on the same Wi-Fi, or type its address from the Elgato Control Center app.";
                return;
            }
            AddressBox.Text = found[0].Address;
            StatusText.Text = found.Count == 1
                ? $"Found {found[0].Name} at {found[0].Address}."
                : "Found " + string.Join(", ", found.Select(f => $"{f.Name} at {f.Address}")) + ". Using the first.";
        }
        finally
        {
            FindButton.IsEnabled = true;
        }
    }

    private async void TestOn_Click(object? sender, RoutedEventArgs e) => await TestAsync(true);
    private async void TestOff_Click(object? sender, RoutedEventArgs e) => await TestAsync(false);

    private async Task TestAsync(bool on)
    {
        StatusText.Text = "Asking the light…";
        var ok = await KeyLightService.SetPowerAsync(AddressBox.Text, on);
        StatusText.Text = ok ? (on ? "The light is on." : "The light is off.") : "The light didn't answer at that address.";
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _store.Load();
        var address = (AddressBox.Text ?? "").Trim();
        settings.KeyLightAddress = address.Length == 0 ? null : address;
        settings.KeyLightOffWhenDone = OffBox.IsChecked == true;
        _store.Save(settings);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
