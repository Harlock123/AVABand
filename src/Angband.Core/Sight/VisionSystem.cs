using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Sight;

/// <summary>A carried light: lights squares within its radius that it has line of sight to.</summary>
public readonly record struct LightSource(Loc Loc, int Radius);

/// <summary>
/// Recomputes what the player can see (Angband update_view). A square is:
/// <list type="bullet">
/// <item><b>View</b> — in line of sight within max sight range;</item>
/// <item><b>Lit</b> — lit this turn by a carried light;</item>
/// <item><b>Seen</b> — in view and lit (by a carried light, its own glow, or bright terrain),
/// and then remembered in the <see cref="KnownMap"/>.</item>
/// </list>
/// Glowing walls are only seen from their lit side, so lit rooms are not revealed from behind.
/// </summary>
public sealed class VisionSystem
{
    private readonly List<Loc> _viewed = [];
    private readonly List<Loc> _lit = [];

    /// <summary>Squares in view after the last update.</summary>
    public IReadOnlyList<Loc> Viewed => _viewed;

    public void Update(Level level, KnownMap known, Loc eye, int maxSight, IReadOnlyList<LightSource> lights, bool blind = false)
    {
        Reset(level);

        foreach (var light in lights)
        {
            if (light.Radius <= 0) continue;
            Fov.Compute(level, light.Loc, light.Radius, p =>
            {
                ref var sq = ref level[p];
                if (sq.Has(SquareFlags.Lit)) return;
                sq.Flags |= SquareFlags.Lit;
                _lit.Add(p);
            });
        }

        Fov.Compute(level, eye, maxSight, p =>
        {
            ref var sq = ref level[p];
            if (sq.Has(SquareFlags.View)) return;
            sq.Flags |= SquareFlags.View;
            _viewed.Add(p);

            if (blind || !IsLitFor(level, p, eye)) return;
            sq.Flags |= SquareFlags.Seen;
            known.Remember(level, p);
        });
    }

    /// <summary>Clears this system's per-turn flags, e.g. before leaving a level.</summary>
    public void Reset(Level level)
    {
        foreach (var p in _viewed)
            if (level.InBounds(p)) level[p].Flags &= ~(SquareFlags.View | SquareFlags.Seen);
        foreach (var p in _lit)
            if (level.InBounds(p)) level[p].Flags &= ~SquareFlags.Lit;
        _viewed.Clear();
        _lit.Clear();
    }

    private static bool IsLitFor(Level level, Loc p, Loc eye)
    {
        ref var sq = ref level[p];
        if (sq.Has(SquareFlags.Lit) || level.Has(p, TerrainFlags.Bright)) return true;
        if (!sq.Has(SquareFlags.Glow)) return false;
        if (!Fov.BlocksSight(level, p)) return true;

        // A glowing wall counts as lit only if a glowing open square lies on the viewer's side.
        var dx = Math.Sign(eye.X - p.X);
        var dy = Math.Sign(eye.Y - p.Y);
        return GlowingOpen(level, new Loc(p.X + dx, p.Y + dy))
               || (dx != 0 && GlowingOpen(level, new Loc(p.X + dx, p.Y)))
               || (dy != 0 && GlowingOpen(level, new Loc(p.X, p.Y + dy)));
    }

    private static bool GlowingOpen(Level level, Loc p) =>
        level.InBounds(p) && level[p].Has(SquareFlags.Glow) && !Fov.BlocksSight(level, p);
}
