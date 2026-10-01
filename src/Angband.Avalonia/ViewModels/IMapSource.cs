using Angband.Core.Geometry;
using Angband.Data.Tiles;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// One drawable map cell. ASCII mode uses the glyph and colours; tile mode looks up
/// <see cref="TileKey"/> (then <see cref="AltTileKey"/>) in the active tileset, drawing the
/// terrain <see cref="UnderKey"/> beneath creatures and traps when the tileset is transparent.
/// <see cref="Sconce"/>: a wall of a lit room with a sconce on it (AVABand's own; the map draws it over the wall).
/// </summary>
public readonly record struct MapCell(
    char Glyph,
    uint Foreground,
    uint Background,
    string TileKey,
    TileLighting Lighting = TileLighting.Lit,
    string? AltTileKey = null,
    string? UnderKey = null,
    TileLighting UnderLighting = TileLighting.Lit,
    bool Sconce = false)
{
    public static readonly MapCell Unknown = new(' ', 0xFF000000, 0xFF000000, "");

    public bool IsUnknown => TileKey.Length == 0;
}

/// <summary>What a map view needs to draw; implemented by view models, consumed by <c>MapView</c>.</summary>
/// <summary>
/// How much a square is darkened (0 none .. 1 black) with light and shadow on; <see cref="Torch"/>
/// squares, lit by the player's own light, flicker and are tinted warm by <see cref="Warmth"/>.
/// </summary>
public readonly record struct MapShade(float Amount, bool Torch = false, float Warmth = 0);

public interface IMapSource
{
    int Width { get; }
    int Height { get; }
    MapCell GetCell(int x, int y);
    /// <summary>The square the camera keeps in view (the player).</summary>
    Loc Focus { get; }
    /// <summary>The look/target cursor, drawn as a highlight.</summary>
    Loc? Cursor => null;
    /// <summary>The current target, drawn as a marker.</summary>
    Loc? Target => null;
    /// <summary>A square to outline lightly (the player, with Angband's highlight_player).</summary>
    Loc? Highlight => null;
    /// <summary>A path to draw over the map: where a click would travel, or where a shot would fly.</summary>
    IReadOnlyList<Loc> ShownPath => [];
    /// <summary>The shown path is a line of fire (it ends where the shot would stop), not a route.</summary>
    bool PathIsAim => false;
    /// <summary>Light and shadow on the map, if on: how dark each square is drawn.</summary>
    bool LightAndShadow => false;
    MapShade ShadeAt(int x, int y) => default;
}
