using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation.Generators;

/// <summary>
/// AVABand's own: the town as your renown has made it (GameSession.Town.cs). Each stage adds to the
/// town the stages before it built — lamps in the square, the rubble cleared and a well dug, market
/// stalls, a fountain and lamps at every door, and last a statue of you — laid out from the town as
/// it stands, with no dice, so the same town and stage always look the same. Stage 0 is the town as
/// it always was: nothing here runs.
/// </summary>
internal static class TownGrowth
{
    /// <summary>The last stage there is.</summary>
    public const int MaxStage = 5;

    /// <summary>How far a lamp's light reaches at night.</summary>
    public const int LampRadius = 3;

    /// <summary>The town's decorations, by terrain id.</summary>
    public const string Lamp = "town_lamp", Well = "town_well", Stall = "market_stall", Fountain = "town_fountain", Statue = "town_statue";

    /// <summary>Builds the town up to <paramref name="stage"/>, a stage at a time.</summary>
    public static void Apply(GenContext ctx, int stage)
    {
        var level = ctx.Level;
        var middle = new Loc(level.Width / 2, level.Height / 2);
        if (stage >= 1)
            foreach (var d in new[] { new Loc(-5, -3), new Loc(5, -3), new Loc(-5, 3), new Loc(5, 3) })
                Place(ctx, Lamp, middle + d);
        if (stage >= 2)
        {
            foreach (var p in level.AllLocs().Where(p => level[p].Feature == ctx.F.PassableRubble).ToList()) ctx.SetFeature(p, ctx.F.Floor);
            Place(ctx, Well, middle + new Loc(-12, 0));
        }
        if (stage >= 3)
            foreach (var d in new[] { new Loc(9, -1), new Loc(11, 1), new Loc(13, -1) })
                Place(ctx, Stall, middle + d);
        if (stage >= 4)
        {
            Place(ctx, Fountain, middle);
            foreach (var door in level.AllLocs().Where(p => level.FeatureAt(p).Has(TerrainFlags.Shop)).ToList())
                Place(ctx, Lamp, door, within: 4);
        }
        if (stage >= 5) Place(ctx, Statue, middle + new Loc(0, -3));
    }

    /// <summary>
    /// A decoration on the open square nearest <paramref name="near"/> (no further than
    /// <paramref name="within"/>) whose eight neighbours are all open street: never against a
    /// building, a door, the stairs or another decoration, so every street stays walkable.
    /// </summary>
    private static void Place(GenContext ctx, string terrainId, Loc near, int within = 12)
    {
        if (!ctx.Data.Terrain.TryGet(terrainId, out var def)) return;
        var level = ctx.Level;
        var spot = level.AllLocs()
            .Where(p => level.InBoundsFully(p) && p.DistanceTo(near) <= within && IsStreet(ctx, p) && level.Neighbors(p).All(n => IsStreet(ctx, n)))
            .OrderBy(p => p.DistanceTo(near)).ThenBy(p => p.Y).ThenBy(p => p.X)
            .Select(p => (Loc?)p).FirstOrDefault();
        if (spot is { } at) ctx.SetFeature(at, def.Index);
    }

    private static bool IsStreet(GenContext ctx, Loc p) => ctx.Level.InBoundsFully(p) && ctx.Level[p].Feature == ctx.F.Floor;

    /// <summary>The squares a town's lamps light at night (within <see cref="LampRadius"/> of one).</summary>
    public static HashSet<Loc> LampLit(Level level)
    {
        var lit = new HashSet<Loc>();
        if (!level.Terrain.TryGet(Lamp, out var lamp)) return lit;
        foreach (var at in level.AllLocs().Where(p => level[p].Feature == lamp.Index).ToList())
            for (var y = at.Y - LampRadius; y <= at.Y + LampRadius; y++)
                for (var x = at.X - LampRadius; x <= at.X + LampRadius; x++)
                    if (level.InBounds(new Loc(x, y)) && new Loc(x, y).DistanceTo(at) <= LampRadius) lit.Add(new Loc(x, y));
        return lit;
    }
}
