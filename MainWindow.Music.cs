using System.Windows;
using BijouHub.Models;
using BijouHub.Services;
using BijouHub.Views;

namespace BijouHub;

// Session music: a playlist for working and one for Pomodoro breaks, opened in the user's own music
// app when the timer starts and swapped as the Pomodoro changes phase. Pausing it is the keyboard's
// play/pause key, as it's the music app that's playing; the session pauses it itself when it ends.
public partial class MainWindow
{
    private readonly PlaylistStore _playlistStore = new();
    private Playlist? _workMusic;
    private Playlist? _breakMusic;
    private bool _musicStarted;

    private Playlist? FindPlaylist(string? id) =>
        string.IsNullOrEmpty(id) ? null : _playlistStore.Load().FirstOrDefault(p => p.Id == id);

    // A playlist id from the start dialog wins ("" there means none); otherwise the mode's own.
    private void ChooseMusic(WorkMode? mode, string? workId, string? breakId)
    {
        _workMusic = FindPlaylist(workId ?? mode?.WorkPlaylistId);
        _breakMusic = FindPlaylist(breakId ?? mode?.BreakPlaylistId);
        _musicStarted = false;
    }

    private void StartSessionMusic()
    {
        if (_workMusic == null) return;
        _musicStarted = true;
        _ = MusicPlayer.PlayAsync(_workMusic);
    }

    // A Pomodoro changed phase: the break playlist for a break, the work one for focus. A phase with
    // no playlist of its own stops what the last one was playing.
    private void PlayPhaseMusic()
    {
        var next = _onBreak ? _breakMusic : _workMusic;
        if (next != null)
        {
            _musicStarted = true;
            _ = MusicPlayer.PlayAsync(next);
        }
        else StopSessionMusic();
    }

    private void StopSessionMusic()
    {
        if (!_musicStarted) return;
        _musicStarted = false;
        _ = MusicPlayer.PauseAsync();
    }

    private void Playlists_Click(object sender, RoutedEventArgs e) =>
        new PlaylistsWindow(_playlistStore) { Owner = DialogOwner }.ShowDialog();
}
