using Avalonia.Interactivity;
using BijouHub.Mac.Services;
using BijouHub.Mac.Views;
using BijouHub.Models;
using BijouHub.Services;

namespace BijouHub.Mac;

// Session music: a playlist for working and one for Pomodoro breaks, opened in the browser
// when the timer starts and swapped as the Pomodoro changes phase. Pausing it is the
// keyboard's play/pause key: macOS gives an app no way to pause another app's audio.
public partial class MainWindow
{
    private readonly PlaylistStore _playlistStore = new();
    private Playlist? _workMusic;
    private Playlist? _breakMusic;

    private Playlist? FindPlaylist(string? id) =>
        string.IsNullOrEmpty(id) ? null : _playlistStore.Load().FirstOrDefault(p => p.Id == id);

    // A playlist id from the start dialog wins ("" there means none); otherwise the mode's own.
    private void ChooseMusic(WorkMode? mode, string? workId, string? breakId)
    {
        _workMusic = FindPlaylist(workId ?? mode?.WorkPlaylistId);
        _breakMusic = FindPlaylist(breakId ?? mode?.BreakPlaylistId);
    }

    private void StartSessionMusic()
    {
        if (_workMusic != null) MacMusic.Play(_workMusic);
    }

    // A Pomodoro changed phase: the break playlist for a break, the work one for focus.
    private void PlayPhaseMusic()
    {
        var next = _onBreak ? _breakMusic : _workMusic;
        if (next != null) MacMusic.Play(next);
    }

    private async void Playlists_Click(object? sender, RoutedEventArgs e) =>
        await new PlaylistsWindow(_playlistStore).ShowDialog(this);
}
