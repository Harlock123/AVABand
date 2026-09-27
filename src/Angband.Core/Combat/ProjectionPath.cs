using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Combat;

[Flags]
public enum PathFlags
{
    None = 0,
    /// <summary>Continue past the target grid until range or a wall (arrows, bolts in flight).</summary>
    Through = 1,
    /// <summary>Stop at the first grid holding a monster (or the player).</summary>
    StopAtCreature = 2,
}

/// <summary>
/// Angband's project_path: the grids a projectile crosses from a source toward a target. The path
/// excludes the source, includes the first wall it hits, and is at most <c>range</c> long, where
/// diagonal steps count one and a half.
/// </summary>
public static class ProjectionPath
{
    public static List<Loc> Compute(Level level, Loc from, Loc to, int range, PathFlags flags = PathFlags.None,
        Func<Loc, bool>? occupied = null)
    {
        var path = new List<Loc>();
        if (from == to || range <= 0) return path;

        var dy = to.Y - from.Y;
        var dx = to.X - from.X;
        var ay = Math.Abs(dy);
        var ax = Math.Abs(dx);
        var sy = Math.Sign(dy);
        var sx = Math.Sign(dx);
        var half = ay * ax;
        var full = half << 1;
        var k = 0; // extra "half" distance from slanted steps

        int x, y, frac, m;
        if (ay > ax)
        {
            frac = ax * ax;
            m = frac << 1;
            y = from.Y + sy;
            x = from.X;
            while (true)
            {
                if (!Step(new Loc(x, y))) break;
                if (m != 0)
                {
                    frac += m;
                    if (frac >= half)
                    {
                        x += sx;
                        frac -= full;
                        k++;
                    }
                }
                y += sy;
            }
        }
        else if (ax > ay)
        {
            frac = ay * ay;
            m = frac << 1;
            x = from.X + sx;
            y = from.Y;
            while (true)
            {
                if (!Step(new Loc(x, y))) break;
                if (m != 0)
                {
                    frac += m;
                    if (frac >= half)
                    {
                        y += sy;
                        frac -= full;
                        k++;
                    }
                }
                x += sx;
            }
        }
        else
        {
            x = from.X + sx;
            y = from.Y + sy;
            while (true)
            {
                if (!Step(new Loc(x, y))) break;
                x += sx;
                y += sy;
            }
        }
        return path;

        // Adds a grid; returns false when the path must end here.
        bool Step(Loc p)
        {
            if (!level.InBounds(p)) return false;
            path.Add(p);
            var n = path.Count;
            var distance = ay > ax || ax > ay ? n + (k >> 1) : n + (n >> 1);
            if (distance >= range) return false;
            if ((flags & PathFlags.Through) == 0 && p == to) return false;
            if (!level.Has(p, TerrainFlags.Project)) return false;
            if ((flags & PathFlags.StopAtCreature) != 0 && occupied?.Invoke(p) == true) return false;
            return true;
        }
    }

    /// <summary>Whether a projection from <paramref name="from"/> reaches <paramref name="to"/> (Angband projectable).</summary>
    public static bool Projectable(Level level, Loc from, Loc to, int range)
    {
        if (from == to) return true;
        var path = Compute(level, from, to, range);
        return path.Count > 0 && path[^1] == to;
    }
}
