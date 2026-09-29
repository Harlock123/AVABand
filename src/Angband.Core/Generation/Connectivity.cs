using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>Region labelling and joining so every walkable part of a level can be reached.</summary>
public static class Connectivity
{
    /// <summary>
    /// Labels 8-connected regions of traversable squares (see <see cref="Level.IsTraversable"/>).
    /// Non-traversable squares get label -1. Returns the size of each region.
    /// </summary>
    public static List<int> LabelRegions(Level level, out int[] labels)
    {
        labels = new int[level.Width * level.Height];
        Array.Fill(labels, -1);
        var sizes = new List<int>();
        var queue = new Queue<Loc>();

        for (var y = 0; y < level.Height; y++)
        for (var x = 0; x < level.Width; x++)
        {
            var start = new Loc(x, y);
            if (labels[y * level.Width + x] != -1 || !level.IsTraversable(start)) continue;

            var id = sizes.Count;
            var size = 0;
            labels[y * level.Width + x] = id;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                size++;
                foreach (var d in DirectionExtensions.Compass)
                {
                    var n = p.Step(d);
                    if (!level.InBounds(n)) continue;
                    var idx = n.Y * level.Width + n.X;
                    if (labels[idx] != -1 || !level.IsTraversable(n)) continue;
                    labels[idx] = id;
                    queue.Enqueue(n);
                }
            }
            sizes.Add(size);
        }
        return sizes;
    }

    /// <summary>All traversable squares reachable from <paramref name="start"/> (8-connected).</summary>
    public static bool[] Reachable(Level level, Loc start)
    {
        var seen = new bool[level.Width * level.Height];
        if (!level.InBounds(start) || !level.IsTraversable(start)) return seen;
        var queue = new Queue<Loc>();
        seen[start.Y * level.Width + start.X] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in DirectionExtensions.Compass)
            {
                var n = p.Step(d);
                if (!level.InBounds(n)) continue;
                var idx = n.Y * level.Width + n.X;
                if (seen[idx] || !level.IsTraversable(n)) continue;
                seen[idx] = true;
                queue.Enqueue(n);
            }
        }
        return seen;
    }

    /// <summary>Regions whose every square is part of a vault (reachable only by tunnelling).</summary>
    public static bool[] SealedVaultPockets(Level level, int[] labels, int regions)
    {
        var sealedPocket = new bool[regions];
        Array.Fill(sealedPocket, true);
        for (var i = 0; i < labels.Length; i++)
            if (labels[i] >= 0 && !level[new Loc(i % level.Width, i / level.Width)].Has(SquareFlags.Vault))
                sealedPocket[labels[i]] = false;
        return sealedPocket;
    }
}
