using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Core.Generation.Generators;

/// <summary>
/// The town: a walled square of streets with one building per shop. Lots are laid out on a grid so
/// every building is ringed by street, which guarantees every entrance can be reached. The layout
/// depends only on the town seed, so the town looks the same on every visit.
/// </summary>
internal sealed class TownGenerator : ILevelGenerator
{
    public string Id => "town";

    public bool Generate(GenContext ctx)
    {
        var town = ctx.Data.Town;
        var rng = ctx.Rng;
        var level = ctx.CreateLevel(town.Width, town.Height);
        level.IsLit = true;
        level.IsKnown = true;

        foreach (var p in level.AllLocs()) ctx.SetFloor(p, SquareFlags.Glow | SquareFlags.Mark);
        ctx.DrawEdge(level.Bounds, ctx.F.Permanent, SquareFlags.WallSolid, SquareFlags.Glow | SquareFlags.Mark);

        var shops = town.Shops.Select(id => ctx.Data.Terrain[id]).ToList();
        var lotCount = town.LotColumns * town.LotRows;
        if (shops.Count > lotCount)
            throw new GameDataException($"Town has {shops.Count} shops but only {lotCount} lots.");

        var lots = Enumerable.Range(0, lotCount).ToList();
        rng.Shuffle(lots);

        var lotWidth = (town.Width - 2) / town.LotColumns;
        var lotHeight = (town.Height - 2) / town.LotRows;
        for (var i = 0; i < shops.Count; i++)
        {
            var lot = lots[i];
            var lotX = 1 + lot % town.LotColumns * lotWidth;
            var lotY = 1 + lot / town.LotColumns * lotHeight;
            BuildShop(ctx, shops[i], new Rect(lotX, lotY, lotX + lotWidth - 1, lotY + lotHeight - 1));
        }

        for (var i = Dice.Parse(town.Rubble).Roll(rng); i > 0; i--)
        {
            var spot = Allocator.FindSpot(ctx, p => level.IsEmptyFloor(p) && !NextToBuilding(level, p));
            if (spot is { } s) ctx.SetFeature(s, ctx.F.PassableRubble);
        }

        var stair = Allocator.FindSpot(ctx, p => level.IsEmptyFloor(p) && !NextToBuilding(level, p));
        if (stair is not { } st) return false;
        ctx.SetFeature(st, ctx.F.DownStair);

        ctx.Level.Population = new PopulationBudget(ctx.Rng.RandRange(4, 8), 0, 0, 0);
        return true;
    }

    /// <summary>A building of permanent wall with its entrance on a non-corner edge; a street ring surrounds it.</summary>
    private static void BuildShop(GenContext ctx, TerrainDef shop, Rect lot)
    {
        var rng = ctx.Rng;
        var width = rng.RandRange(6, Math.Min(12, lot.Width - 4));
        var height = rng.RandRange(4, Math.Min(7, lot.Height - 3));
        var x = rng.RandRange(lot.X1 + 1, lot.X2 - 1 - width + 1);
        var y = rng.RandRange(lot.Y1 + 1, lot.Y2 - 1 - height + 1);
        var building = Rect.FromSize(x, y, width, height);

        foreach (var p in building.Cells())
            ctx.SetWall(p, ctx.F.Permanent, SquareFlags.WallSolid, SquareFlags.Glow | SquareFlags.Mark);

        var entrance = rng.RandInt0(4) switch
        {
            0 => new Loc(rng.RandRange(building.X1 + 1, building.X2 - 1), building.Y1),
            1 => new Loc(rng.RandRange(building.X1 + 1, building.X2 - 1), building.Y2),
            2 => new Loc(building.X1, rng.RandRange(building.Y1 + 1, building.Y2 - 1)),
            _ => new Loc(building.X2, rng.RandRange(building.Y1 + 1, building.Y2 - 1)),
        };
        ctx.SetFeature(entrance, shop.Index);
    }

    private static bool NextToBuilding(Level level, Loc p) =>
        level.Neighbors(p).Any(n => level.IsPermanent(n) && level.InBoundsFully(n));
}
