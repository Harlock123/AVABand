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

    /// <summary>
    /// Joins every region to the largest one by digging the cheapest path (walls cost more than
    /// open ground; vault walls cost most, so joins prefer existing doors). Pierced room walls become doors.
    /// </summary>
    internal static bool EnsureConnected(GenContext ctx, int maxJoins = 400)
    {
        var level = ctx.Level;
        for (var join = 0; join < maxJoins; join++)
        {
            var sizes = LabelRegions(level, out var labels);
            if (sizes.Count <= 1) return sizes.Count == 1;

            // Pockets entirely inside a vault are sealed on purpose (Angband: dig in to reach them).
            var sealedPocket = SealedVaultPockets(level, labels, sizes.Count);
            var main = -1;
            for (var i = 0; i < sizes.Count; i++)
                if (!sealedPocket[i] && (main < 0 || sizes[i] > sizes[main])) main = i;
            if (main < 0) return true;
            var source = -1; // lowest-numbered region that is neither the main one nor sealed
            for (var i = 0; i < sizes.Count && source < 0; i++)
                if (i != main && !sealedPocket[i]) source = i;
            if (source < 0) return true;

            if (!JoinRegion(ctx, labels, source, main)) return false;
        }
        return false;
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

    private static bool JoinRegion(GenContext ctx, int[] labels, int source, int target)
    {
        var level = ctx.Level;
        var w = level.Width;
        var dist = new int[labels.Length];
        var prev = new int[labels.Length];
        Array.Fill(dist, int.MaxValue);
        Array.Fill(prev, -1);
        var pq = new PriorityQueue<int, int>();

        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] != source) continue;
            dist[i] = 0;
            pq.Enqueue(i, 0);
        }

        var found = -1;
        while (pq.TryDequeue(out var cur, out var d))
        {
            if (d > dist[cur]) continue;
            if (labels[cur] == target) { found = cur; break; }

            var p = new Loc(cur % w, cur / w);
            foreach (var dir in DirectionExtensions.Orthogonal)
            {
                var n = p.Step(dir);
                if (!level.InBoundsFully(n)) continue;
                var cost = StepCost(level, n);
                if (cost < 0) continue;
                var ni = n.Y * w + n.X;
                var nd = d + cost;
                if (nd >= dist[ni]) continue;
                dist[ni] = nd;
                prev[ni] = cur;
                pq.Enqueue(ni, nd);
            }
        }

        if (found < 0) return false;

        for (var i = prev[found]; i >= 0 && labels[i] != source; i = prev[i])
        {
            var p = new Loc(i % w, i / w);
            if (level.IsTraversable(p)) continue;
            ref var sq = ref level[p];
            if (sq.Has(SquareFlags.WallOuter)) ctx.PlaceRandomDoor(p);
            else ctx.SetFloor(p, sq.Flags & (SquareFlags.Room | SquareFlags.Vault | SquareFlags.Glow));
        }
        return true;
    }

    private static int StepCost(Level level, Loc p)
    {
        if (level.IsTraversable(p)) return 1;
        if (level.IsPermanent(p)) return -1;
        ref var sq = ref level[p];
        if (sq.Has(SquareFlags.Vault)) return 8;
        if (sq.Has(SquareFlags.WallInner)) return 4;
        if (sq.Has(SquareFlags.WallOuter)) return 3;
        return 2;
    }
}
