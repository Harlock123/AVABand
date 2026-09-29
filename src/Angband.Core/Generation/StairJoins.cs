using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>
/// Persistent levels' connecting stairs (Angband 4.2 birth_levels_persist; gen-cave.c
/// handle_level_stairs): each join becomes a staircase. The classic generator has already built a
/// staircase room there (gen-room.c build_staircase) that its tunnels reach; elsewhere (caverns) a
/// join in rock is dug through to the nearest open square. The level's own stairs of that direction
/// are removed, so the joins are its only way up (or down).
/// </summary>
internal static class StairJoins
{
    public static void Apply(GenContext ctx, IReadOnlyList<StairJoin> joins)
    {
        foreach (var down in new[] { false, true })
        {
            var level = ctx.Level;
            var mine = joins.Where(j => j.Down == down).ToList();
            if (mine.Count == 0) continue;
            var feature = down ? ctx.F.DownStair : ctx.F.UpStair;
            var flag = down ? TerrainFlags.DownStair : TerrainFlags.UpStair;
            foreach (var p in level.FindFeature(flag).ToList()) ctx.SetFeature(p, ctx.F.Floor);
            // A join that doesn't fit (in permanent rock — a vault's wall — or off the level) goes to the
            // nearest square that does, so every way down from the level above still comes out here.
            var placed = new HashSet<Loc>();
            foreach (var join in mine)
                if (NearestFit(ctx, join.Loc, placed) is { } at)
                {
                    Carve(ctx, at, feature);
                    placed.Add(at);
                }
        }
    }

    private static readonly Loc[] Cardinal = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];

    private static bool Fits(GenContext ctx, Loc p) =>
        ctx.Level.InBoundsFully(p) && !ctx.Level.FeatureAt(p).Has(TerrainFlags.Permanent) && !ctx.Level[p].Has(SquareFlags.Vault);

    /// <summary>The join's own square if it fits, else the nearest that does (ring by ring), not already taken.</summary>
    private static Loc? NearestFit(GenContext ctx, Loc at, HashSet<Loc> taken)
    {
        for (var r = 0; r < Math.Max(ctx.Level.Width, ctx.Level.Height); r++)
        {
            Loc? best = null;
            for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                var p = at + new Loc(dx, dy);
                if (taken.Contains(p) || !Fits(ctx, p)) continue;
                if (best is null || p.DistanceTo(at) < best.Value.DistanceTo(at)) best = p;
            }
            if (best is not null) return best;
        }
        return null;
    }

    /// <summary>Puts the staircase in, then digs the shortest way through rock to open floor.</summary>
    private static void Carve(GenContext ctx, Loc at, ushort feature)
    {
        var level = ctx.Level;
        var wasOpen = level.FeatureAt(at).Has(TerrainFlags.Passable);
        ctx.SetFeature(at, feature);
        if (wasOpen) return;

        // Breadth-first through anything but permanent rock and the map edge, to the nearest walkable square.
        var from = new Dictionary<Loc, Loc> { [at] = at };
        var queue = new Queue<Loc>([at]);
        Loc? reached = null;
        while (queue.Count > 0 && reached is null)
        {
            var p = queue.Dequeue();
            foreach (var step in Cardinal)
            {
                var n = p + step;
                if (!level.InBoundsFully(n) || from.ContainsKey(n) || level.FeatureAt(n).Has(TerrainFlags.Permanent)) continue;
                from[n] = p;
                if (level.FeatureAt(n).Has(TerrainFlags.Passable)) { reached = n; break; }
                queue.Enqueue(n);
            }
        }
        if (reached is not { } end) return; // walled in: the level's check will ask for another
        for (var p = from[end]; p != at; p = from[p])
            if (!level.FeatureAt(p).Has(TerrainFlags.Passable)) ctx.SetFeature(p, ctx.F.Floor);
    }
}
