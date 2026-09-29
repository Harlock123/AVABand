using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>Finding spots and placing the player in AVABand's town.</summary>
internal static class Allocator
{
    /// <summary>Random search, then an exhaustive (still deterministic) scan as a fallback.</summary>
    public static Loc? FindSpot(GenContext ctx, Func<Loc, bool> ok, int tries = 1000)
    {
        var level = ctx.Level;
        for (var i = 0; i < tries; i++)
        {
            var p = new Loc(ctx.Rng.RandRange(1, level.Width - 2), ctx.Rng.RandRange(1, level.Height - 2));
            if (ok(p)) return p;
        }

        var candidates = new List<Loc>();
        for (var y = 1; y < level.Height - 1; y++)
        for (var x = 1; x < level.Width - 1; x++)
        {
            var p = new Loc(x, y);
            if (ok(p)) candidates.Add(p);
        }
        return candidates.Count == 0 ? null : ctx.Rng.Pick(candidates);
    }

    private static bool IsPlainSpot(GenContext ctx, Loc p) =>
        ctx.Level.IsEmptyFloor(p) && !ctx.Level[p].HasAny(SquareFlags.Vault | SquareFlags.NoStairs);

    /// <summary>
    /// Picks the player's starting square. With connected stairs the player arrives on a staircase
    /// leading back the way they came — up after going down, down after going up (Angband 4.2's
    /// "create a way back", gen-util.c new_player_spot).
    /// </summary>
    public static Loc? PlacePlayer(GenContext ctx, StairArrival arrival, bool connectedStairs)
    {
        var level = ctx.Level;

        if (ctx.Depth == 0 && arrival == StairArrival.Ascended)
        {
            var stair = level.FindFeature(TerrainFlags.DownStair).Cast<Loc?>().FirstOrDefault();
            if (stair is not null) return stair;
        }

        var spot = FindSpot(ctx, p => IsPlainSpot(ctx, p) && !level.Has(p, TerrainFlags.Shop));
        if (spot is not { } s) return null;

        if (connectedStairs && ctx.Depth > 0 && arrival != StairArrival.None)
        {
            var wayBackDown = arrival == StairArrival.Ascended && ctx.Depth < ctx.Data.Constants.MaxDepth;
            ctx.SetFeature(s, wayBackDown ? ctx.F.DownStair : ctx.F.UpStair);
        }
        return s;
    }
}
