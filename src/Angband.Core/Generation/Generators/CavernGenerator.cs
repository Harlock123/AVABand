using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation.Generators;

/// <summary>
/// Natural caverns via cellular automata: random fill, smoothing, pocket removal and joining.
/// Caverns are dark and crowded (1.5x monsters), like Angband's cavern levels.
/// </summary>
internal sealed class CavernGenerator : ILevelGenerator
{
    public string Id => "cavern";

    public bool Generate(GenContext ctx)
    {
        var p = ctx.Profile.Cavern;
        var rng = ctx.Rng;
        var height = rng.RandRange(p.MinHeight, p.MaxHeight);
        var width = rng.RandRange(p.MinWidth, p.MaxWidth);
        var level = ctx.CreateLevel(width, height);

        var wall = new bool[width, height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            wall[x, y] = x == 0 || y == 0 || x == width - 1 || y == height - 1 || rng.Percent(p.FillPercent);

        for (var pass = 0; pass < p.SmoothingPasses; pass++)
            wall = Smooth(wall, width, height);

        foreach (var loc in level.AllLocs())
        {
            if (wall[loc.X, loc.Y]) ctx.SetWall(loc, ctx.F.Granite, SquareFlags.None);
            else ctx.SetFloor(loc);
        }
        ctx.DrawEdge(level.Bounds, ctx.F.Permanent, SquareFlags.WallSolid);

        RemoveSmallPockets(ctx, p.MinPocketSize);

        var open = level.AllLocs().Count(level.IsFloor);
        if (open * 100 < width * height * p.MinOpenPercent) return false;

        Streamers.Build(ctx);
        if (!Connectivity.EnsureConnected(ctx)) return false;
        return Allocator.AllocStandard(ctx, populationScale: 1.5);
    }

    /// <summary>A cell becomes wall when 5+ of the 9 cells around it (itself included) are wall.</summary>
    private static bool[,] Smooth(bool[,] wall, int width, int height)
    {
        var next = new bool[width, height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
            {
                next[x, y] = true;
                continue;
            }
            var count = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (wall[x + dx, y + dy]) count++;
            next[x, y] = count >= 5;
        }
        return next;
    }

    private static void RemoveSmallPockets(GenContext ctx, int minSize)
    {
        var level = ctx.Level;
        var sizes = Connectivity.LabelRegions(level, out var labels);
        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] < 0 || sizes[labels[i]] >= minSize) continue;
            ctx.SetWall(new Loc(i % level.Width, i / level.Width), ctx.F.Granite, SquareFlags.None);
        }
    }
}
