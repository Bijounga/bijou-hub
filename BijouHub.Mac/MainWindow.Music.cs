using Avalonia.Interactivity;
using BijouHub.Mac.Services;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// Session music: a playlist for working and one for Pomodoro breaks, opened in the user's own music
// app when the timer starts and swapped as the Pomodoro changes phase. The session pauses Spotify
// itself when it ends; a browser tab is paused with the keyboard's play/pause key.
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
        _ = MacMusic.PlayAsync(_workMusic);
    }

    // A Pomodoro changed phase: the break playlist for a break, the work one for focus. A phase with
    // no playlist of its own stops what the last one was playing.
    private void PlayPhaseMusic()
    {
        var next = _onBreak ? _breakMusic : _workMusic;
        if (next != null)
        {
            _musicStarted = true;
            _ = MacMusic.PlayAsync(next);
        }
        else StopSessionMusic();
    }

    private void StopSessionMusic()
    {
        if (!_musicStarted) return;
        _musicStarted = false;
        _ = MacMusic.PauseAsync();
    }

    private async void Playlists_Click(object? sender, RoutedEventArgs e) =>
        await new PlaylistsWindow(_playlistStore).ShowDialog(this);
}
