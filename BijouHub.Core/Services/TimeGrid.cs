namespace BijouHub.Services;

// The maths behind the day/week time grid, kept free of any UI so Windows and Mac behave the
// same and it can be tested without a mouse: where a pixel falls in time, how overlapping
// blocks share a day's width, and the drag gestures (make, move, resize an event).

public sealed record GridGeometry(int Days, double Width, double HourHeight, int SnapMinutes = 15)
{
    public double ColumnWidth => Days <= 0 ? 0 : Width / Days;
    public double MinuteHeight => HourHeight / 60.0;
    public double Height => HourHeight * 24;

    public int DayAt(double x) => ColumnWidth <= 0 ? 0 : Math.Clamp((int)Math.Floor(x / ColumnWidth), 0, Days - 1);

    // The minute of the day at a height, snapped down (or up, or to the nearest snap).
    public int MinuteAt(double y, Snap snap = Snap.Down)
    {
        var minutes = y / MinuteHeight / SnapMinutes;
        var steps = snap switch { Snap.Up => Math.Ceiling(minutes), Snap.Nearest => Math.Round(minutes), _ => Math.Floor(minutes) };
        return Math.Clamp((int)(steps * SnapMinutes), 0, 24 * 60);
    }

    public double YOf(int minute) => minute * MinuteHeight;
}

public enum Snap { Down, Up, Nearest }

public static class TimeGridLanes
{
    // Overlapping blocks sit side by side. Returns, for each input (in order), its lane and how
    // many lanes its cluster of overlapping blocks needs.
    public static (int Lane, int Lanes)[] Pack(IReadOnlyList<(int Start, int End)> blocks)
    {
        var result = new (int, int)[blocks.Count];
        var order = Enumerable.Range(0, blocks.Count).OrderBy(i => blocks[i].Start).ThenByDescending(i => blocks[i].End).ToList();

        var cluster = new List<int>();
        var clusterEnd = int.MinValue;
        var laneEnds = new List<int>();
        var lanes = new int[blocks.Count];

        void Flush()
        {
            foreach (var i in cluster) result[i] = (lanes[i], laneEnds.Count);
            cluster.Clear();
            laneEnds.Clear();
            clusterEnd = int.MinValue;
        }

        foreach (var i in order)
        {
            if (cluster.Count > 0 && blocks[i].Start >= clusterEnd) Flush();
            var lane = laneEnds.FindIndex(end => end <= blocks[i].Start);
            if (lane < 0)
            {
                laneEnds.Add(blocks[i].End);
                lane = laneEnds.Count - 1;
            }
            else
            {
                laneEnds[lane] = blocks[i].End;
            }
            lanes[i] = lane;
            cluster.Add(i);
            clusterEnd = Math.Max(clusterEnd, blocks[i].End);
        }
        Flush();
        return result;
    }
}

// A block on the grid that can be clicked, dragged or resized.
public sealed record GridItem(string Id, int Day, int StartMinute, int EndMinute, int Lane = 0, int Lanes = 1);

public enum GridTarget { Empty, Body, ResizeHandle }
public enum GestureKind { None, Click, Create, Move, Resize }

// What a drag looks like right now (to draw), and what it ended as (to apply).
public sealed record GestureState(GestureKind Kind, string? Id, int Day, int StartMinute, int EndMinute);

// Mouse down → move → up on the grid. Feed it pixel positions; it says what the gesture is.
//   empty space, dragged  → Create a block over the dragged stretch
//   empty space, clicked  → Create a one-hour block there
//   a block, dragged      → Move it (to another time or day)
//   a block's bottom edge → Resize it
//   a block, clicked      → Click (open it)
public sealed class TimeGridGesture
{
    public const double DragThreshold = 4;
    public const double HandleHeight = 7;
    public const int DefaultMinutes = 60;

    private enum Mode { Idle, Empty, Body, Handle }

    private readonly GridGeometry _grid;
    private readonly IReadOnlyList<GridItem> _items;
    private Mode _mode;
    private GridItem? _item;
    private double _downX, _downY;
    private int _downDay, _anchor;
    private bool _dragging;
    private GestureState _state = new(GestureKind.None, null, 0, 0, 0);

    public TimeGridGesture(GridGeometry grid, IReadOnlyList<GridItem> items)
    {
        _grid = grid;
        _items = items;
    }

    public bool Active => _mode != Mode.Idle;

    // Later items sit on top.
    public GridTarget HitTest(double x, double y, out GridItem? item)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var candidate = _items[i];
            var laneWidth = _grid.ColumnWidth / candidate.Lanes;
            var left = candidate.Day * _grid.ColumnWidth + candidate.Lane * laneWidth;
            var top = _grid.YOf(candidate.StartMinute);
            var bottom = _grid.YOf(candidate.EndMinute);
            if (x < left || x >= left + laneWidth || y < top || y >= bottom) continue;
            item = candidate;
            return y >= bottom - HandleHeight && bottom - top > HandleHeight * 2 ? GridTarget.ResizeHandle : GridTarget.Body;
        }
        item = null;
        return GridTarget.Empty;
    }

    public void Begin(double x, double y)
    {
        _downX = x;
        _downY = y;
        _dragging = false;
        _state = new GestureState(GestureKind.None, null, 0, 0, 0);
        _downDay = _grid.DayAt(x);
        var target = HitTest(x, y, out _item);
        _mode = target switch { GridTarget.Body => Mode.Body, GridTarget.ResizeHandle => Mode.Handle, _ => Mode.Empty };
        _anchor = _grid.MinuteAt(y, Snap.Down);
    }

    // The gesture as it stands, or null while the pointer hasn't moved far enough to be a drag.
    public GestureState? Move(double x, double y)
    {
        if (_mode == Mode.Idle) return null;
        if (!_dragging && Math.Abs(x - _downX) < DragThreshold && Math.Abs(y - _downY) < DragThreshold) return null;
        _dragging = true;
        _state = Compute(x, y);
        return _state;
    }

    public GestureState End(double x, double y)
    {
        if (_mode == Mode.Idle) return new GestureState(GestureKind.None, null, 0, 0, 0);
        GestureState result;
        if (_dragging)
        {
            result = Compute(x, y);
        }
        else if (_mode == Mode.Empty)
        {
            var start = Math.Min(_anchor, 24 * 60 - DefaultMinutes);
            result = new GestureState(GestureKind.Create, null, _downDay, start, start + DefaultMinutes);
        }
        else
        {
            result = new GestureState(GestureKind.Click, _item!.Id, _item.Day, _item.StartMinute, _item.EndMinute);
        }
        Cancel();
        return result;
    }

    public void Cancel()
    {
        _mode = Mode.Idle;
        _item = null;
        _dragging = false;
    }

    private GestureState Compute(double x, double y)
    {
        var snap = _grid.SnapMinutes;
        switch (_mode)
        {
            case Mode.Empty:
            {
                var anchorEnd = Math.Min(_anchor + snap, 24 * 60);
                var anchorStart = anchorEnd - snap;
                if (y >= _grid.YOf(anchorEnd))
                    return new GestureState(GestureKind.Create, null, _downDay, anchorStart, Math.Max(anchorEnd, _grid.MinuteAt(y, Snap.Up)));
                if (y < _grid.YOf(anchorStart))
                    return new GestureState(GestureKind.Create, null, _downDay, Math.Min(anchorStart, _grid.MinuteAt(y, Snap.Down)), anchorEnd);
                return new GestureState(GestureKind.Create, null, _downDay, anchorStart, anchorEnd);
            }
            case Mode.Body:
            {
                var item = _item!;
                var length = item.EndMinute - item.StartMinute;
                var shift = (int)Math.Round((y - _downY) / _grid.MinuteHeight / snap) * snap;
                var start = Math.Clamp(item.StartMinute + shift, 0, 24 * 60 - length);
                var day = Math.Clamp(item.Day + (_grid.DayAt(x) - _downDay), 0, _grid.Days - 1);
                return new GestureState(GestureKind.Move, item.Id, day, start, start + length);
            }
            case Mode.Handle:
            {
                var item = _item!;
                var end = Math.Clamp(_grid.MinuteAt(y, Snap.Nearest), item.StartMinute + snap, 24 * 60);
                return new GestureState(GestureKind.Resize, item.Id, item.Day, item.StartMinute, end);
            }
            default:
                return new GestureState(GestureKind.None, null, 0, 0, 0);
        }
    }
}
