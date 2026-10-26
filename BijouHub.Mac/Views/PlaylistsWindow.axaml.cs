using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BijouHub.Mac.Services;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

// The saved playlists: add a link, remove one, or play one to try it. Changes save as they're made.
public partial class PlaylistsWindow : Window
{
    private readonly PlaylistStore _store;
    private readonly ObservableCollection<Playlist> _playlists;

    public PlaylistsWindow() : this(new PlaylistStore()) { }

    public PlaylistsWindow(PlaylistStore store)
    {
        InitializeComponent();
        _store = store;
        _playlists = new ObservableCollection<Playlist>(store.Load());
        PlaylistList.ItemsSource = _playlists;
        _playlists.CollectionChanged += (_, _) => { _store.Save(_playlists.ToList()); ShowEmpty(); };
        ShowEmpty();
        Opened += (_, _) => UrlBox.Focus();
    }

    private void ShowEmpty() => EmptyText.IsVisible = _playlists.Count == 0;

    private void Box_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Add_Click(sender, e);
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (!MusicLinks.TryNormalize(UrlBox.Text, out var url))
        {
            await PromptWindow.Notice(this, "Playlists", "Paste a playlist link, like one from Spotify or YouTube.");
            UrlBox.Focus();
            return;
        }

        var name = (NameBox.Text ?? "").Trim();
        _playlists.Add(new Playlist { Name = name.Length > 0 ? name : MusicLinks.SuggestName(url), Url = url });
        UrlBox.Text = "";
        NameBox.Text = "";
        UrlBox.Focus();
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: Playlist playlist }) _playlists.Remove(playlist);
    }

    private void Play_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: Playlist playlist }) _ = MacMusic.PlayAsync(playlist);
    }

    private void Done_Click(object? sender, RoutedEventArgs e) => Close();
}
