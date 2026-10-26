using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using BijouHub.Mac.Services;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac.Views;

// The saved playlists, on a Work tab and a Break tab: add a link, remove one, play one to try it, and
// choose whether each tab plays in shuffled order. Changes save as they're made.
public partial class PlaylistsWindow : Window
{
    private readonly PlaylistStore _store;
    private readonly AppSettingsStore _settingsStore;
    private readonly List<Playlist> _playlists;
    private PlaylistKind _kind = PlaylistKind.Work;
    private bool _loadingShuffle;

    public PlaylistsWindow() : this(new PlaylistStore(), new AppSettingsStore()) { }

    public PlaylistsWindow(PlaylistStore store, AppSettingsStore settingsStore)
    {
        InitializeComponent();
        _store = store;
        _settingsStore = settingsStore;
        _playlists = store.Load();
        ShowTab();
        Opened += (_, _) => UrlBox.Focus();
    }

    private void WorkTab_Click(object? sender, RoutedEventArgs e) => SwitchTab(PlaylistKind.Work);
    private void BreakTab_Click(object? sender, RoutedEventArgs e) => SwitchTab(PlaylistKind.Break);

    private void SwitchTab(PlaylistKind kind)
    {
        _kind = kind;
        ShowTab();
        UrlBox.Focus();
    }

    private void ShowTab()
    {
        var work = _kind == PlaylistKind.Work;
        foreach (var (text, on) in new[] { (WorkTabText, work), (BreakTabText, !work) })
        {
            text.Foreground = this.FindResource(on ? "AccentBrush" : "MutedTextBrush") as IBrush;
            text.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
        }

        HintText.Text = work
            ? "What plays while you work. Pick one when you start a timer, or let it go through all of them. On a Mac, you move on to the next one yourself."
            : "What plays on a Pomodoro break, something calmer. It goes back to the Work playlists when the break ends.";

        var settings = _settingsStore.Load();
        _loadingShuffle = true;
        ShuffleBox.IsChecked = work ? settings.ShuffleWorkMusic : settings.ShuffleBreakMusic;
        _loadingShuffle = false;

        var shown = _playlists.Where(p => p.Kind == _kind).ToList();
        PlaylistList.ItemsSource = shown;
        EmptyText.IsVisible = shown.Count == 0;
    }

    private void Shuffle_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loadingShuffle) return;
        var settings = _settingsStore.Load();
        if (_kind == PlaylistKind.Work) settings.ShuffleWorkMusic = ShuffleBox.IsChecked == true;
        else settings.ShuffleBreakMusic = ShuffleBox.IsChecked == true;
        _settingsStore.Save(settings);
    }

    private void Box_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Add_Click(sender, e);
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (!AddButton.IsEnabled) return;
        if (!MusicLinks.TryNormalize(UrlBox.Text, out var url))
        {
            await PromptWindow.Notice(this, "Playlists", "Paste a playlist link, like one from YouTube.");
            UrlBox.Focus();
            return;
        }

        var name = (NameBox.Text ?? "").Trim();
        if (name.Length == 0)
        {
            // a YouTube link is named after its video; the lookup takes a moment
            AddButton.IsEnabled = UrlBox.IsEnabled = NameBox.IsEnabled = false;
            name = await MusicLinks.FetchTitleAsync(url) ?? MusicLinks.SuggestName(url);
            AddButton.IsEnabled = UrlBox.IsEnabled = NameBox.IsEnabled = true;
        }

        _playlists.Add(new Playlist { Name = name, Url = url, Kind = _kind });
        _store.Save(_playlists);
        UrlBox.Text = "";
        NameBox.Text = "";
        ShowTab();
        UrlBox.Focus();
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: Playlist playlist }) return;
        _playlists.Remove(playlist);
        _store.Save(_playlists);
        ShowTab();
    }

    private void Play_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: Playlist playlist }) MacMusic.Play(playlist);
    }

    private void Done_Click(object? sender, RoutedEventArgs e) => Close();
}
