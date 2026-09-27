using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>Structural checks every generated level must pass.</summary>
public static class LevelValidator
{
    public static IReadOnlyList<string> Validate(Level level, Loc start, GameConstants constants)
    {
        var errors = new List<string>();

        foreach (var p in level.Bounds.Edge())
        {
            if (!level.IsPermanent(p) || level.IsPassable(p))
            {
                errors.Add($"Edge square {p} is not a permanent wall.");
                break;
            }
        }

        if (!level.InBounds(start) || !level.IsPassable(start))
            errors.Add($"Player start {start} is not passable.");

        var up = level.FindFeature(TerrainFlags.UpStair).Count();
        var down = level.FindFeature(TerrainFlags.DownStair).Count();
        if (level.Depth == 0 && up > 0) errors.Add("The town must not have up staircases.");
        if (level.Depth > 0 && up == 0) errors.Add("No up staircase.");
        if (level.Depth < constants.MaxDepth && down == 0) errors.Add("No down staircase.");

        var reachable = Connectivity.Reachable(level, start);
        var unreachable = 0;
        Loc? firstUnreachable = null;
        foreach (var p in level.AllLocs())
        {
            // Vault pockets may be sealed: they are reached by tunnelling.
            if (!level.IsTraversable(p) || reachable[p.Y * level.Width + p.X] || level[p].Has(SquareFlags.Vault)) continue;
            unreachable++;
            firstUnreachable ??= p;
        }
        if (unreachable > 0)
            errors.Add($"{unreachable} walkable squares are unreachable from the start (first at {firstUnreachable}).");

        return errors;
    }
}
