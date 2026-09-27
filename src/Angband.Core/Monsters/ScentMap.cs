using Angband.Core.Combat;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Monsters;

/// <summary>
/// The trail the player leaves (Angband cave->scent). Each time the player moves, squares within
/// two grids that the player can reach with a projection are stamped with the current move
/// number. Monsters with a sense of smell follow the freshest stamps, which lets them track a
/// player they can neither see nor hear.
/// </summary>
public sealed class ScentMap
{
    public const int Radius = 2;
    private int[] _stamps = [];
    private int _width;

    /// <summary>Number of player moves recorded so far.</summary>
    public int Now { get; private set; }

    public void Reset(Level level)
    {
        _width = level.Width;
        _stamps = new int[level.Width * level.Height];
        Now = 0;
    }

    public void Lay(Level level, Loc player)
    {
        if (_stamps.Length != level.Width * level.Height) Reset(level);
        Now++;
        for (var dy = -Radius; dy <= Radius; dy++)
        for (var dx = -Radius; dx <= Radius; dx++)
        {
            var p = new Loc(player.X + dx, player.Y + dy);
            if (!level.InBounds(p) || !level.IsPassable(p)) continue;
            if (p != player && !ProjectionPath.Projectable(level, player, p, Radius + 1)) continue;
            _stamps[p.Y * _width + p.X] = Now;
        }
    }

    /// <summary>Raw stamps, for saving.</summary>
    public ReadOnlySpan<int> Raw => _stamps;

    /// <summary>Restores a saved trail.</summary>
    public void Restore(int width, int[] stamps, int now)
    {
        _width = width;
        _stamps = stamps;
        Now = now;
    }

    /// <summary>The move number the square was last scented (0 = never).</summary>
    public int Stamp(Loc p) => _stamps.Length == 0 ? 0 : _stamps[p.Y * _width + p.X];

    /// <summary>How many moves old the scent here is, or <see cref="int.MaxValue"/> if there is none.</summary>
    public int Age(Loc p) => Stamp(p) == 0 ? int.MaxValue : Now - Stamp(p);
}
