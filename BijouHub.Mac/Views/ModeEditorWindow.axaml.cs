using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BijouHub.Models;

namespace BijouHub.Mac.Views;

// Edits a mode's name and what it opens. Changes apply to the mode only on Save.
public partial class ModeEditorWindow : Window
{
    private readonly WorkMode _mode;
    private readonly ObservableCollection<LaunchItem> _items;

    public ModeEditorWindow() : this(new WorkMode()) { }

    public ModeEditorWindow(WorkMode mode)
    {
        InitializeComponent();
        _mode = mode;
        NameBox.Text = mode.Name;
        _items = new ObservableCollection<LaunchItem>(mode.LaunchItems);
        ItemsList.ItemsSource = _items;
        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private async void AddApp_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an app or file",
            AllowMultiple = true,
            SuggestedStartLocation = OperatingSystem.IsMacOS() ? await StorageProvider.TryGetFolderFromPathAsync("/Applications") : null
        });
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;
            var isApp = path.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            _items.Add(new LaunchItem { Type = isApp ? LaunchItemType.Application : LaunchItemType.File, Path = path.TrimEnd('/') });
        }
    }

    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a folder", AllowMultiple = true });
        foreach (var folder in folders)
            if (folder.TryGetLocalPath() is { Length: > 0 } path)
                _items.Add(new LaunchItem { Type = LaunchItemType.Folder, Path = path });
    }

    private async void AddLink_Click(object? sender, RoutedEventArgs e)
    {
        var url = await PromptWindow.Ask(this, "Add link", "Web address to open:", "https://");
        if (url == null || url == "https://") return;
        if (!url.Contains("://")) url = "https://" + url;
        _items.Add(new LaunchItem { Type = LaunchItemType.Url, Path = url });
    }

    private void RemoveItem_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is LaunchItem item) _items.Remove(item);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        _mode.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "New Mode" : NameBox.Text.Trim();
        _mode.LaunchItems = _items.ToList();
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
