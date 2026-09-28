using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>
/// Persistent levels' connecting stairs (Angband 4.2 birth_levels_persist; gen-cave.c
/// handle_level_stairs, gen-room.c build_staircase): each join becomes a staircase, walled round as
/// a one-square "staircase room" where it stands in rock and tunnelled through to the level; the
/// level's own stairs of that direction are removed, so the joins are its only way up (or down).
/// </summary>
internal static class StairJoins
{
    public static void Apply(GenContext ctx, IReadOnlyList<StairJoin> joins)
    {
        foreach (var down in new[] { false, true })
        {
            var level = ctx.Level;
            var mine = joins.Where(j => j.Down == down && level.InBoundsFully(j.Loc)
                                        && !level.FeatureAt(j.Loc).Has(TerrainFlags.Permanent)).ToList();
            if (mine.Count == 0) continue; // none fit this level: keep its own stairs
            var feature = down ? ctx.F.DownStair : ctx.F.UpStair;
            var flag = down ? TerrainFlags.DownStair : TerrainFlags.UpStair;
            foreach (var p in level.FindFeature(flag).ToList()) ctx.SetFeature(p, ctx.F.Floor);
            foreach (var join in mine) Carve(ctx, join.Loc, feature);
        }
    }

    private static readonly Loc[] Cardinal = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];

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
