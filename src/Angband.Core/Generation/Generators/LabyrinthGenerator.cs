using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation.Generators;

/// <summary>
/// A perfect maze (randomised depth-first carving) on odd coordinates. Shallow labyrinths tend to
/// be lit, mapped and made of diggable granite; deep ones are dark, unknown and permanent-walled.
/// </summary>
internal sealed class LabyrinthGenerator : ILevelGenerator
{
    public string Id => "labyrinth";

    public bool Generate(GenContext ctx)
    {
        var p = ctx.Profile.Labyrinth;
        var rng = ctx.Rng;
        var growth = rng.RandInt0(ctx.Depth / Math.Max(1, p.GrowthDepth) + 1);
        var height = Math.Min(p.BaseHeight + 2 * growth, 65);
        var width = Math.Min(p.BaseWidth + 10 * growth, 197);
        if (height % 2 == 0) height--;
        if (width % 2 == 0) width--;

        var lit = rng.RandInt0(ctx.Depth) < 25 || rng.OneIn(2);
        var known = lit && rng.RandInt0(ctx.Depth) < 25;
        var soft = rng.RandInt0(ctx.Depth) < 35 || rng.OneIn(3);

        var level = ctx.CreateLevel(width, height);
        level.IsLit = lit;
        level.IsKnown = known;
        var wallFeature = soft ? ctx.F.Granite : ctx.F.Permanent;
        ctx.Fill(level.Bounds, wallFeature, SquareFlags.WallSolid);
        ctx.DrawEdge(level.Bounds, ctx.F.Permanent, SquareFlags.WallSolid);

        var light = GenContext.Light(lit);
        var cellsX = (width - 1) / 2;
        var cellsY = (height - 1) / 2;
        var visited = new bool[cellsX, cellsY];
        var stack = new Stack<(int X, int Y)>();
        var start = (X: rng.RandInt0(cellsX), Y: rng.RandInt0(cellsY));
        visited[start.X, start.Y] = true;
        ctx.SetFloor(CellLoc(start.X, start.Y), light);
        stack.Push(start);

        var options = new List<(int X, int Y)>(4);
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Peek();
            options.Clear();
            if (cx > 0 && !visited[cx - 1, cy]) options.Add((cx - 1, cy));
            if (cx < cellsX - 1 && !visited[cx + 1, cy]) options.Add((cx + 1, cy));
            if (cy > 0 && !visited[cx, cy - 1]) options.Add((cx, cy - 1));
            if (cy < cellsY - 1 && !visited[cx, cy + 1]) options.Add((cx, cy + 1));
            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }

            var (nx, ny) = rng.Pick(options);
            visited[nx, ny] = true;
            var between = new Loc(cx + nx + 1, cy + ny + 1);
            ctx.SetFloor(CellLoc(nx, ny), light);
            ctx.SetFloor(between, light);
            if (rng.Percent(p.DoorChance)) ctx.PlaceRandomDoor(between);
            stack.Push((nx, ny));
        }

        if (known)
            foreach (var loc in level.AllLocs())
                level[loc].Flags |= SquareFlags.Mark;

        if (!Allocator.AllocStandard(ctx)) return false;
        return true;

        static Loc CellLoc(int x, int y) => new(2 * x + 1, 2 * y + 1);
    }
}
