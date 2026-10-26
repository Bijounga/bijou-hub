using BijouHub.Models;

namespace BijouHub.Services;

// Which playlist plays next out of one tab's playlists: in the list's order, or in random order
// (each played once before any comes round again) when shuffling. Always a different one from the
// last, unless it's the only one.
public class MusicQueue
{
    // The "play all of them" choice, in place of one playlist's id.
    public const string AllId = "*";

    private readonly List<Playlist> _pool;
    private readonly bool _shuffle;
    private readonly Random _random;
    private readonly List<Playlist> _bag = new();
    private int _next;

    public MusicQueue(IEnumerable<Playlist> pool, bool shuffle, Random? random = null)
    {
        _pool = pool.ToList();
        _shuffle = shuffle;
        _random = random ?? new Random();
    }

    public Playlist? Current { get; private set; }

    // What a music picker offers for one tab: no music, all of them, or one in particular.
    public static List<Playlist> Choices(IEnumerable<Playlist> playlists, PlaylistKind kind)
    {
        var choices = new List<Playlist>
        {
            new() { Id = "", Name = "No music" },
            new() { Id = AllId, Name = kind == PlaylistKind.Work ? "All work playlists" : "All break playlists" }
        };
        choices.AddRange(playlists.Where(p => p.Kind == kind));
        return choices;
    }

    // The playlist a phase opens with: the one asked for, or (for "all") the next one in line.
    public Playlist? Begin(string? id)
    {
        var index = string.IsNullOrEmpty(id) || id == AllId ? -1 : _pool.FindIndex(p => p.Id == id);
        if (index < 0) return Next();

        _bag.Remove(_pool[index]);
        _next = index + 1;
        return Current = _pool[index];
    }

    // The playlist to move on to when the current one ends.
    public Playlist? Next()
    {
        if (_pool.Count == 0) return Current = null;
        if (_pool.Count == 1) return Current = _pool[0];

        Playlist pick;
        if (_shuffle)
        {
            if (_bag.Count == 0)
            {
                _bag.AddRange(_pool.OrderBy(_ => _random.Next()));
                if (_bag[0] == Current) (_bag[0], _bag[^1]) = (_bag[^1], _bag[0]);
            }
            pick = _bag[0];
            _bag.RemoveAt(0);
        }
        else
        {
            pick = _pool[_next % _pool.Count];
            _next++;
            if (pick == Current)
            {
                pick = _pool[_next % _pool.Count];
                _next++;
            }
        }
        return Current = pick;
    }
}
