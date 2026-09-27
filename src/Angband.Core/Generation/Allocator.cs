using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>Scatters stairs, rubble and traps, and places the player.</summary>
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

    /// <summary>Stairs prefer corridor-like squares (two or more adjacent walls), as in Angband.</summary>
    public static int AllocStairs(GenContext ctx, ushort feature, int count)
    {
        var placed = 0;
        for (var i = 0; i < count; i++)
        {
            var spot = FindSpot(ctx, p => IsPlainSpot(ctx, p) && ctx.Level.CountAdjacentWalls(p) >= 2)
                       ?? FindSpot(ctx, p => IsPlainSpot(ctx, p));
            if (spot is not { } s) break;
            ctx.SetFeature(s, feature);
            placed++;
        }
        return placed;
    }

    public static void AllocRubble(GenContext ctx, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var spot = FindSpot(ctx, p => IsPlainSpot(ctx, p)
                                          && !ctx.Level[p].Has(SquareFlags.Room)
                                          && ctx.Level.CountAdjacentWalls(p) >= 2, tries: 300);
            if (spot is not { } s) break;
            ctx.SetFeature(s, ctx.Rng.OneIn(3) ? ctx.F.PassableRubble : ctx.F.Rubble);
        }
    }

    public static void AllocTraps(GenContext ctx, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var spot = FindSpot(ctx, p => IsPlainSpot(ctx, p) && !ctx.Level[p].Has(SquareFlags.NoTrap), tries: 300);
            if (spot is not { } s) break;
            ctx.PlaceTrap(s);
        }
    }

    /// <summary>Stairs, rubble, traps and the population budget, driven by the profile.</summary>
    public static bool AllocStandard(GenContext ctx, double populationScale = 1.0)
    {
        var a = ctx.Profile.Allocation;
        var up = AllocStairs(ctx, ctx.F.UpStair, Math.Max(1, Dice.Parse(a.UpStairs).Roll(ctx.Rng)));
        var down = ctx.Depth >= ctx.Data.Constants.MaxDepth
            ? 1
            : AllocStairs(ctx, ctx.F.DownStair, Math.Max(1, Dice.Parse(a.DownStairs).Roll(ctx.Rng)));
        if (up == 0 || down == 0) return false;

        AllocRubble(ctx, Dice.Parse(a.Rubble).Roll(ctx.Rng));
        AllocTraps(ctx, ctx.Rng.RandInt1(a.TrapBase + ctx.Depth / Math.Max(1, a.TrapDepthDivisor)));
        SetPopulation(ctx, populationScale);
        return true;
    }

    public static void SetPopulation(GenContext ctx, double scale = 1.0)
    {
        var a = ctx.Profile.Allocation;
        var k = Math.Min(ctx.Depth / 3, 10);
        var monsters = a.MonsterMin + ctx.Rng.RandInt1(a.MonsterRandom) + k;
        ctx.Level.Population = new PopulationBudget(
            (int)(monsters * scale),
            Math.Max(0, ctx.Rng.Normal(a.RoomObjects, 3)),
            Math.Max(0, ctx.Rng.Normal(a.AnywhereObjects, 3)),
            Math.Max(0, ctx.Rng.Normal(a.Gold, 3)));
    }

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
