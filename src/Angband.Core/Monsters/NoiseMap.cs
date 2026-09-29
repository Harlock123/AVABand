using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Monsters;

/// <summary>
/// How far the player's noise has carried to each square (Angband make_noise, cave->noise): a
/// breadth-first flow from the player over the whole level, through everything but NO_FLOW
/// terrain (rock and rubble), one step louder at a time — four while covering tracks. Monsters
/// that can hear the player follow it down, around walls instead of into them.
/// </summary>
public sealed class NoiseMap
{
    /// <summary>Silence: a square the noise never reached (and the player's own, as in 4.2.5).</summary>
    public const int Unreached = 0;
    private int[] _noise = [];
    private int _width;
    private readonly Queue<Loc> _queue = new(); // kept between updates: this runs every player turn

    /// <param name="increment">What each step adds (Angband: 1, or 4 under covertracks).</param>
    public void Update(Level level, Loc source, int increment = 1)
    {
        _width = level.Width;
        if (_noise.Length != level.Width * level.Height) _noise = new int[level.Width * level.Height];
        Array.Fill(_noise, Unreached);

        var queue = _queue;
        queue.Clear();
        _noise[source.Y * _width + source.X] = 0;
        queue.Enqueue(source);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var next = _noise[p.Y * _width + p.X] + increment;
            foreach (var dir in DirectionExtensions.Compass)
            {
                var n = p.Step(dir);
                if (!level.InBounds(n) || _noise[n.Y * _width + n.X] != Unreached || n == source) continue;
                if (level.FeatureAt(n).Has(TerrainFlags.NoFlow)) continue;
                _noise[n.Y * _width + n.X] = next;
                queue.Enqueue(n);
            }
        }
    }

    /// <summary>A new level starts silent (Angband cave_new): nothing is heard until the next world turn.</summary>
    public void Reset(Level level)
    {
        _width = level.Width;
        _noise = new int[level.Width * level.Height];
        Array.Fill(_noise, Unreached);
    }

    /// <summary>Raw values, for saving.</summary>
    public ReadOnlySpan<int> Raw => _noise;

    /// <summary>Restores a saved map.</summary>
    public void Restore(int width, int[] noise)
    {
        _width = width;
        _noise = noise;
    }

    public int this[Loc p]
    {
        get
        {
            var i = p.Y * _width + p.X;
            return p.X < 0 || p.X >= _width || i < 0 || i >= _noise.Length ? Unreached : _noise[i];
        }
    }
}
