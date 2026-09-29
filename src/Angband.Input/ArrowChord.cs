using Angband.Core.Geometry;

namespace Angband.Input;

/// <summary>
/// Two arrow keys held together move diagonally (Up and Right: north-east), for keyboards without a
/// keypad. A press waits a moment (<see cref="Grace"/>) for its partner before it moves straight;
/// letting go sooner moves at once. Held down, the key's own repeats go on moving — diagonally while
/// both are down. Pure logic: the window feeds it key presses, releases and a clock.
/// </summary>
public sealed class ArrowChord
{
    /// <summary>How long a lone arrow waits for a second one.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(50);

    private readonly HashSet<Direction> _held = [];
    private TimeSpan? _pendingUntil;

    /// <summary>A move to make.</summary>
    public event Action<Direction>? Move;

    /// <summary>Whether a lone press is waiting out the grace (the window should call <see cref="Tick"/> then).</summary>
    public bool IsWaiting => _pendingUntil is not null;

    /// <summary>An arrow pressed (or its key repeating while held).</summary>
    public void Down(Direction dir, TimeSpan now)
    {
        var repeat = !_held.Add(dir);
        if (Combined() is { } diagonal && _held.Count == 2)
        {
            _pendingUntil = null;
            Move?.Invoke(diagonal);
            return;
        }
        if (_pendingUntil is not null) return; // (still waiting for a partner)
        if (repeat) Move?.Invoke(dir); // held on its own: it repeats
        else _pendingUntil = now + Grace;
    }

    /// <summary>An arrow let go: a lone press still waiting moves now.</summary>
    public void Up(Direction dir, TimeSpan now)
    {
        if (_pendingUntil is not null && _held.Count == 1 && _held.Contains(dir))
        {
            _pendingUntil = null;
            Move?.Invoke(dir);
        }
        _held.Remove(dir);
    }

    /// <summary>The grace is over: a lone arrow still held moves straight.</summary>
    public void Tick(TimeSpan now)
    {
        if (_pendingUntil is not { } until || now < until) return;
        _pendingUntil = null;
        if (_held.Count == 1) Move?.Invoke(_held.First());
    }

    /// <summary>Forget everything held (the window lost focus, a menu opened).</summary>
    public void Reset()
    {
        _held.Clear();
        _pendingUntil = null;
    }

    /// <summary>Two held arrows at right angles as one diagonal (null otherwise).</summary>
    private Direction? Combined()
    {
        if (_held.Count != 2) return null;
        int dx = 0, dy = 0;
        foreach (var d in _held)
        {
            var o = d.Offset();
            dx += o.X;
            dy += o.Y;
        }
        return dx != 0 && dy != 0 ? DirectionExtensions.FromOffset(dx, dy) : null;
    }
}
