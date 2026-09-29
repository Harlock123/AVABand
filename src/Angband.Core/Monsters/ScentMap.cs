using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Monsters;

/// <summary>
/// The trail the player leaves (Angband update_scent, cave->scent): every square's scent ages by
/// one each turn, and new scent is laid in the 5×5 around the player — 0 on the player's own
/// square (which no monster smells), 1 next to it, 2 two away — where it can spread over the floor
/// from a fresher square, and not on NO_SCENT terrain. Monsters smell scent younger than their
/// sense of smell reaches, and follow the freshest.
/// </summary>
public sealed class ScentMap
{
    private static readonly int[,] Strength = { { 2, 2, 2, 2, 2 }, { 2, 1, 1, 1, 2 }, { 2, 1, 0, 1, 2 }, { 2, 1, 1, 1, 2 }, { 2, 2, 2, 2, 2 } };
    private int[] _scent = [];
    private int _width;

    /// <summary>Turns of scent laid so far (kept for saves).</summary>
    public int Now { get; private set; }

    public void Reset(Level level)
    {
        _width = level.Width;
        _scent = new int[level.Width * level.Height];
        Now = 0;
    }

    /// <summary>Angband update_scent: age every trace, then (unless <paramref name="covertracks"/>) lay new scent.</summary>
    public void Lay(Level level, Loc player, bool covertracks = false)
    {
        if (_scent.Length != level.Width * level.Height) Reset(level);
        Now++;
        for (var i = 0; i < _scent.Length; i++)
            if (_scent[i] > 0) _scent[i]++;
        if (covertracks) return;
        for (var y = 0; y < 5; y++)
        for (var x = 0; x < 5; x++)
        {
            var g = new Loc(player.X + x - 2, player.Y + y - 2);
            if (!level.InBounds(g) || level.FeatureAt(g).Has(TerrainFlags.NoScent)) continue;
            var fresh = Strength[y, x];
            var add = x == 2 && y == 2;
            foreach (var dir in DirectionExtensions.Compass)
            {
                var adj = g.Step(dir);
                if (level.InBounds(adj) && _scent[adj.Y * _width + adj.X] == fresh - 1) add = true;
            }
            if (add) _scent[g.Y * _width + g.X] = fresh;
        }
    }

    /// <summary>Raw values, for saving.</summary>
    public ReadOnlySpan<int> Raw => _scent;

    /// <summary>Restores a saved trail.</summary>
    public void Restore(int width, int[] scent, int now)
    {
        _width = width;
        _scent = scent;
        Now = now;
    }

    /// <summary>The scent's age on the square (Angband cave->scent): 0 for none.</summary>
    public int this[Loc p] => _scent.Length == 0 ? 0 : _scent[p.Y * _width + p.X];
}
