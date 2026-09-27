using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Sight;

/// <summary>
/// Field of view by symmetric shadowcasting (Albert Ford's algorithm) with exact integer slopes.
/// Symmetric means that if floor square A can see floor square B, B can see A. Monsters rely on
/// this: a monster can see the player exactly when its square is in the player's view, as in Angband.
/// Squares whose terrain lacks <see cref="TerrainFlags.Los"/> block sight but are themselves visible.
/// </summary>
public static class Fov
{
    /// <summary>
    /// Calls <paramref name="reveal"/> for every square visible from <paramref name="origin"/> within
    /// <paramref name="radius"/> (Angband distance). A square may be reported more than once.
    /// </summary>
    public static void Compute(Level level, Loc origin, int radius, Action<Loc> reveal)
    {
        if (!level.InBounds(origin)) return;
        reveal(origin);
        if (radius <= 0) return;

        var scan = new Scanner(level, origin, radius, reveal);
        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            scan.Quadrant = quadrant;
            scan.Scan(1, new Slope(-1, 1), new Slope(1, 1));
        }
    }

    /// <summary>The set of squares visible from <paramref name="origin"/>, as a row-major mask.</summary>
    public static bool[] VisibleMask(Level level, Loc origin, int radius)
    {
        var mask = new bool[level.Width * level.Height];
        Compute(level, origin, radius, p => mask[p.Y * level.Width + p.X] = true);
        return mask;
    }

    public static bool BlocksSight(Level level, Loc p) => !level.InBounds(p) || !level.Has(p, TerrainFlags.Los);

    /// <summary>An exact rational slope <c>Num / Den</c> with positive denominator.</summary>
    private readonly record struct Slope(long Num, long Den);

    private sealed class Scanner(Level level, Loc origin, int radius, Action<Loc> reveal)
    {
        public int Quadrant { get; set; }

        /// <summary>Maps (depth, column) in the current quadrant to a map location.</summary>
        private Loc Transform(int depth, int col) => Quadrant switch
        {
            0 => new Loc(origin.X + col, origin.Y - depth), // north
            1 => new Loc(origin.X + depth, origin.Y + col), // east
            2 => new Loc(origin.X + col, origin.Y + depth), // south
            _ => new Loc(origin.X - depth, origin.Y + col), // west
        };

        public void Scan(int depth, Slope start, Slope end)
        {
            if (depth > radius) return;

            // min_col = round_ties_up(depth * start), max_col = round_ties_down(depth * end)
            var minCol = FloorDiv(2 * depth * start.Num + start.Den, 2 * start.Den);
            var maxCol = CeilDiv(2 * depth * end.Num - end.Den, 2 * end.Den);

            bool? prevWall = null;
            for (var col = minCol; col <= maxCol; col++)
            {
                var p = Transform(depth, (int)col);
                var wall = BlocksSight(level, p);
                var symmetric = col * start.Den >= depth * start.Num && col * end.Den <= depth * end.Num;

                if ((wall || symmetric) && level.InBounds(p) && origin.DistanceTo(p) <= radius) reveal(p);

                if (prevWall == true && !wall)
                    start = TileSlope(depth, col);
                if (prevWall == false && wall)
                    Scan(depth + 1, start, TileSlope(depth, col));
                prevWall = wall;
            }

            if (prevWall == false) Scan(depth + 1, start, end);
        }

        /// <summary>Slope of a tile's leading edge: (2*col - 1) / (2*depth).</summary>
        private static Slope TileSlope(int depth, long col) => new(2 * col - 1, 2L * depth);

        private static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        private static long CeilDiv(long a, long b) => -FloorDiv(-a, b);
    }
}
