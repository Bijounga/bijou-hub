using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Views;

namespace BijouHub;

// Session music: the work playlists while working and the break playlists on a Pomodoro break, opened
// in the browser when the timer starts and swapped as the Pomodoro changes phase. When a video runs
// to its end the next playlist (random, if shuffling) takes over. Pausing it is the keyboard's
// play/pause key, as it's the browser that's playing; the session pauses it itself when it ends.
public partial class MainWindow
{
    private readonly PlaylistStore _playlistStore = new();
    private MusicQueue? _workQueue;
    private MusicQueue? _breakQueue;
    private string? _workStart;
    private string? _breakStart;
    private bool _musicStarted;
    private DispatcherTimer? _musicWatch;
    private DateTime _musicQuietUntil;
    private bool _musicChecking;

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
        _musicStarted = false;
    }

    private static MusicQueue? QueueFor(List<Playlist> playlists, PlaylistKind kind, string? start, bool shuffle) =>
        string.IsNullOrEmpty(start) ? null
        : new MusicQueue(playlists.Where(p => p.Kind == kind || p.Id == start), shuffle);

    private void StartSessionMusic()
    {
        if (_workQueue == null) return;
        PlayMusic(_workQueue.Begin(_workStart));
        _musicWatch ??= CreateMusicWatch();
        _musicWatch.Start();
    }

    private DispatcherTimer CreateMusicWatch()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += async (_, _) =>
        {
            var queue = _onBreak ? _breakQueue : _workQueue;
            if (!_musicStarted || queue == null || _musicChecking || DateTime.Now < _musicQuietUntil) return;

            _musicChecking = true;
            try
            {
                if (await MusicPlayer.HasEndedAsync() && queue.Next() is { } next) PlayMusic(next);
            }
            finally
            {
                _musicChecking = false;
            }
        };
        return timer;
    }

    private void PlayMusic(Playlist? playlist)
    {
        if (playlist == null) return;
        _musicStarted = true;
        _musicQuietUntil = DateTime.Now.AddSeconds(25); // the new tab needs a moment before it's what's playing
        _ = MusicPlayer.PlayAsync(playlist);
    }

    // A Pomodoro changed phase: the break playlists for a break, the work ones for focus. A phase
    // with none of its own stops what the last one was playing.
    private void PlayPhaseMusic()
    {
        var queue = _onBreak ? _breakQueue : _workQueue;
        if (queue != null) PlayMusic(queue.Begin(_onBreak ? _breakStart : _workStart));
        else StopSessionMusic();
    }

    private void StopSessionMusic()
    {
        _musicWatch?.Stop();
        if (!_musicStarted) return;
        _musicStarted = false;
        _ = MusicPlayer.PauseAsync();
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

    private static void FillMusicCombo(ComboBox combo, List<Playlist> choices, string? pick)
    {
        combo.Items.Clear();
        foreach (var choice in choices) combo.Items.Add(choice);
        combo.SelectedItem = choices.FirstOrDefault(p => p.Id == (pick ?? "")) ?? choices[0];
    }

    private void Playlists_Click(object sender, RoutedEventArgs e)
    {
        new PlaylistsWindow(_playlistStore, _settingsStore) { Owner = DialogOwner }.ShowDialog();
        if (ModeDetailPanel.Visibility == Visibility.Visible && ModesList.SelectedItem is WorkMode mode) LoadModeMusic(mode, keepPicks: true);
    }
}
