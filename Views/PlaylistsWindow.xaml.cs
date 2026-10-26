using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Views;

// The saved playlists: add a link, remove one, or play one to try it. Changes save as they're made.
public partial class PlaylistsWindow : Window
{
    private readonly PlaylistStore _store;
    private readonly ObservableCollection<Playlist> _playlists;

    public PlaylistsWindow(PlaylistStore store)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _store = store;
        _playlists = new ObservableCollection<Playlist>(store.Load());
        PlaylistList.ItemsSource = _playlists;
        _playlists.CollectionChanged += (_, _) => { Save(); ShowEmpty(); };
        ShowEmpty();
        Loaded += (_, _) => UrlBox.Focus();
    }

    private void Save() => _store.Save(_playlists.ToList());

    private void ShowEmpty() => EmptyText.Visibility = _playlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Add_Click(sender, e);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!MusicLinks.TryNormalize(UrlBox.Text, out var url))
        {
            MessageBox.Show("Paste a playlist link, like one from YouTube.", "Playlists", MessageBoxButton.OK, MessageBoxImage.Information);
            UrlBox.Focus();
            return;
        }

        var name = NameBox.Text.Trim();
        _playlists.Add(new Playlist { Name = name.Length > 0 ? name : MusicLinks.SuggestName(url), Url = url });
        UrlBox.Clear();
        NameBox.Clear();
        UrlBox.Focus();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Playlist playlist }) _playlists.Remove(playlist);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Playlist playlist }) _ = MusicPlayer.PlayAsync(playlist);
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
