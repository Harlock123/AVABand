using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Monsters;

/// <summary>
/// How far the player's noise has travelled to each square (Angband cave->noise): a breadth-first
/// flow from the player through passable squares and doors. Monsters that can hear the player walk
/// down this gradient, which takes them around walls instead of into them.
/// </summary>
public sealed class NoiseMap
{
    public const int Unreached = int.MaxValue;
    private int[] _distance = [];
    private int _width;

    /// <summary>Flow limit in grids (Angband: max_flow_depth).</summary>
    public const int MaxFlow = 32;

    public void Update(Level level, Loc source)
    {
        _width = level.Width;
        if (_distance.Length != level.Width * level.Height) _distance = new int[level.Width * level.Height];
        Array.Fill(_distance, Unreached);

        var queue = new Queue<Loc>();
        _distance[source.Y * _width + source.X] = 0;
        queue.Enqueue(source);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var d = _distance[p.Y * _width + p.X];
            if (d >= MaxFlow) continue;
            foreach (var dir in DirectionExtensions.Compass)
            {
                var n = p.Step(dir);
                if (!level.InBounds(n) || _distance[n.Y * _width + n.X] != Unreached) continue;
                if (!level.FeatureAt(n).HasAny(TerrainFlags.Passable | TerrainFlags.DoorClosed)) continue;
                _distance[n.Y * _width + n.X] = d + 1;
                queue.Enqueue(n);
            }
        }
    }

    public int this[Loc p] => _distance.Length == 0 ? Unreached : _distance[p.Y * _width + p.X];
}
