using Avalonia.Interactivity;
using BijouHub.Mac.Services;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// Session music: the work playlists while working and the break playlists on a Pomodoro break, opened
// in the browser when the timer starts and swapped as the Pomodoro changes phase. Unlike on Windows
// it can't tell when a video has ended (macOS gives an app no view of another app's playback), so
// moving on to the next playlist is up to the user. Pausing it is the keyboard's play/pause key.
public partial class MainWindow
{
    private readonly PlaylistStore _playlistStore = new();
    private MusicQueue? _workQueue;
    private MusicQueue? _breakQueue;
    private string? _workStart;
    private string? _breakStart;

    // A playlist id from the start dialog wins ("" there means none); otherwise the mode's own.
    // Either may be MusicQueue.AllId: every playlist on that tab, one after another.
    private void ChooseMusic(WorkMode? mode, string? workId, string? breakId)
    {
        var settings = _settingsStore.Load();
        var playlists = _playlistStore.Load();
        _workStart = workId ?? mode?.WorkPlaylistId;
        _breakStart = breakId ?? mode?.BreakPlaylistId;
        _workQueue = QueueFor(playlists, PlaylistKind.Work, _workStart, settings.ShuffleWorkMusic);
        _breakQueue = QueueFor(playlists, PlaylistKind.Break, _breakStart, settings.ShuffleBreakMusic);
    }

    private static MusicQueue? QueueFor(List<Playlist> playlists, PlaylistKind kind, string? start, bool shuffle) =>
        string.IsNullOrEmpty(start) ? null
        : new MusicQueue(playlists.Where(p => p.Kind == kind || p.Id == start), shuffle);

    private void StartSessionMusic()
    {
        if (_workQueue?.Begin(_workStart) is { } playlist) MacMusic.Play(playlist);
    }

    // A Pomodoro changed phase: the break playlists for a break, the work ones for focus.
    private void PlayPhaseMusic()
    {
        var queue = _onBreak ? _breakQueue : _workQueue;
        if (queue?.Begin(_onBreak ? _breakStart : _workStart) is { } playlist) MacMusic.Play(playlist);
    }

    // ---------- The pickers on a mode page: music chosen by mood, each time a timer is started ----------

    // Null until the pickers are filled; "" is "No music".
    private string? ModeWorkMusicId => (ModeWorkMusicCombo.SelectedItem as Playlist)?.Id;
    private string? ModeBreakMusicId => (ModeBreakMusicCombo.SelectedItem as Playlist)?.Id;

    // Starts on the mode's own playlists; keepPicks holds what is chosen now (playlists were just edited).
    private void LoadModeMusic(WorkMode mode, bool keepPicks = false)
    {
        var playlists = _playlistStore.Load();
        FillMusicCombo(ModeWorkMusicCombo, MusicQueue.Choices(playlists, PlaylistKind.Work), keepPicks ? ModeWorkMusicId : mode.WorkPlaylistId);
        FillMusicCombo(ModeBreakMusicCombo, MusicQueue.Choices(playlists, PlaylistKind.Break), keepPicks ? ModeBreakMusicId : mode.BreakPlaylistId);
    }

    private static void FillMusicCombo(Avalonia.Controls.ComboBox combo, List<Playlist> choices, string? pick)
    {
        combo.ItemsSource = choices;
        combo.SelectedItem = choices.FirstOrDefault(p => p.Id == (pick ?? "")) ?? choices[0];
    }

    private async void Playlists_Click(object? sender, RoutedEventArgs e)
    {
        await new PlaylistsWindow(_playlistStore, _settingsStore).ShowDialog(this);
        if (ModePanel.IsVisible && _shownMode is WorkMode mode) LoadModeMusic(mode, keepPicks: true);
    }
}
