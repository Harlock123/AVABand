using Angband.Core.Geometry;
using Angband.Data.Tiles;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// The whole level at a glance (Angband 'M', ui-map.c display_map): the map squeezed into blocks
/// of <see cref="Block"/>×<see cref="Block"/> squares, each showing its most important square — the
/// player, then anything standing or lying on the ground (a monster, an object, a trap), then the
/// terrain with the highest display priority (stairs and doors over walls over floor).
/// </summary>
public sealed class OverviewMapSource(MainWindowViewModel game) : IMapSource
{
    /// <summary>Angband display_map: anything drawn over the terrain counts this much.</summary>
    public const int OnTopPriority = 20;

    /// <summary>Squares per block along each side (1 shows the level at full size).</summary>
    public int Block { get; set; } = 1;

    public int Width => (game.Width + Block - 1) / Block;
    public int Height => (game.Height + Block - 1) / Block;

    public bool UseTiles => game.UseTiles;
    public TilesetManifest? Tileset => game.SelectedTileset;
    /// <summary>The map font, at most 11 points: small letters squeeze the level less.</summary>
    public double FontSize => Math.Min(game.MapFontSize, 11);

    public Loc Focus => new(game.Game.Player.Position.X / Block, game.Game.Player.Position.Y / Block);

    /// <summary>The player's block, outlined.</summary>
    public Loc? Highlight => Focus;

    /// <summary>A pin anywhere in the block.</summary>
    public bool HasPin(int x, int y)
    {
        for (var dy = 0; dy < Block; dy++)
        for (var dx = 0; dx < Block; dx++)
            if (game.HasPin(x * Block + dx, y * Block + dy)) return true;
        return false;
    }

    public MapCell GetCell(int x, int y)
    {
        var best = MapCell.Unknown;
        var bestPriority = -1;
        for (var dy = 0; dy < Block; dy++)
        for (var dx = 0; dx < Block; dx++)
        {
            var p = new Loc(x * Block + dx, y * Block + dy);
            if (p.X >= game.Width || p.Y >= game.Height) continue;
            var cell = game.GetCell(p.X, p.Y);
            if (cell.IsUnknown) continue;
            var priority = game.OverviewPriority(p, cell);
            if (priority > bestPriority)
            {
                best = cell;
                bestPriority = priority;
            }
        }
        return best;
    }
}
