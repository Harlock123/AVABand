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

    /// <summary>
    /// AVABand's "no diagonal squeezes" option: wherever two walkable squares touch only at a corner
    /// and nothing else joins the parts they belong to without a diagonal step, one of the two walls
    /// beside that corner becomes floor, so everywhere can be walked to with the four arrow keys.
    /// Permanent walls and vaults are left alone. Returns how many squares were opened.
    /// </summary>
    public static int OpenDiagonalSqueezes(Level level)
    {
        int w = level.Width, h = level.Height;
        // 4-connected parts, joined as walls are opened (union-find over squares).
        var parent = new int[w * h];
        for (var i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }
        void Union(int a, int b) => parent[Find(a)] = Find(b);
        bool Walkable(int x, int y) => level.IsTraversable(new Loc(x, y));
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (!Walkable(x, y)) continue;
            if (x + 1 < w && Walkable(x + 1, y)) Union(y * w + x, y * w + x + 1);
            if (y + 1 < h && Walkable(x, y + 1)) Union(y * w + x, (y + 1) * w + x);
        }

        bool Openable(Loc c) => level.InBoundsFully(c) && !level.IsTraversable(c) && !level.IsPermanent(c)
                                && !level[c].Has(SquareFlags.Vault);
        void Open(Loc c)
        {
            ref var sq = ref level[c];
            sq.Feature = level.Terrain.Ids.Floor;
            sq.Flags &= ~SquareFlags.AnyWallMarker;
            sq.Trap = 0;
            sq.LockPower = 0;
            var i = c.Y * w + c.X;
            foreach (var d in DirectionExtensions.Orthogonal)
            {
                var n = c.Step(d);
                if (level.InBounds(n) && level.IsTraversable(n)) Union(i, n.Y * w + n.X);
            }
        }

        var opened = 0;
        for (var y = 0; y + 1 < h; y++)
        for (var x = 0; x < w; x++)
        foreach (var dx in (ReadOnlySpan<int>)[-1, 1])
        {
            int x2 = x + dx, y2 = y + 1;
            if (x2 < 0 || x2 >= w || !Walkable(x, y) || !Walkable(x2, y2)) continue;
            if (Find(y * w + x) == Find(y2 * w + x2)) continue;
            // Open the corner beside the upper square, or else the one beside the lower.
            var a = new Loc(x2, y);
            var b = new Loc(x, y2);
            if (Openable(a)) Open(a);
            else if (Openable(b)) Open(b);
            else continue;
            opened++;
        }
        return opened;
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
