using System.Globalization;
using Angband.Core.Definitions;
using Angband.Data.Tiles;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// Turns game things into <see cref="MapCell"/>s: ASCII glyph and colour from the game data, plus the
/// namespaced tile keys (<c>terrain:</c>, <c>monster:</c>, <c>trap:</c>, <c>player</c>) tilesets use.
/// </summary>
public sealed class MapCellBuilder
{
    public const uint Black = 0xFF000000;
    private const string TorchColor = "LightYellow";
    private readonly Dictionary<string, uint> _colors;

    public MapCellBuilder(GameData data)
    {
        _colors = data.Colors.ToDictionary(kv => kv.Key, kv => ParseColor(kv.Value), StringComparer.OrdinalIgnoreCase);
    }

    public static string TerrainKey(TerrainDef feature) => "terrain:" + feature.Id;

    // Display options (Angband solid_walls, hybrid_walls, view_yellow_light, purple_uniques,
    // hp_changes_color); they change the ASCII look only.
    public bool SolidWalls { get; set; }
    public bool HybridWalls { get; set; }
    public bool YellowLight { get; set; } = true;
    public bool PurpleUniques { get; set; }
    public bool HpChangesColor { get; set; } = true;

    /// <summary>
    /// A terrain cell. Remembered scenery is dimmed in ASCII (stairs, doors and shops stay bright);
    /// torch-lit floor is tinted with view_yellow_light. Walls can be drawn as solid blocks
    /// (solid_walls) or on a shaded background (hybrid_walls).
    /// </summary>
    public MapCell Terrain(TerrainDef feature, TileLighting lighting)
    {
        var fg = Color(feature.Color);
        if (lighting == TileLighting.Dark && !feature.Has(TerrainFlags.Interesting)) fg = Dim(fg);
        else if (YellowLight && lighting == TileLighting.Torch && feature.Has(TerrainFlags.Floor)) fg = Color(TorchColor);
        var wall = feature.HasAny(TerrainFlags.Wall | TerrainFlags.Rock) && !feature.Has(TerrainFlags.Passable)
                   && !feature.HasAny(TerrainFlags.DoorAny | TerrainFlags.Rubble);
        if (wall && SolidWalls) return new MapCell(' ', fg, fg, TerrainKey(feature), lighting);
        if (wall && HybridWalls) return new MapCell(feature.Glyph, fg, Dim(Dim(fg)), TerrainKey(feature), lighting);
        return new MapCell(feature.Glyph, fg, Black, TerrainKey(feature), lighting);
    }

    /// <summary>The player; with hp_changes_color the @ goes from white to red as hit points fall (Angband ui-map.c).</summary>
    public MapCell Player(MapCell under, int hp = 1, int maxHp = 1)
    {
        var color = !HpChangesColor || maxHp <= 0 ? "White" : (Math.Clamp(hp, 0, maxHp) * 10 / maxHp) switch
        {
            >= 9 => "White",
            >= 7 => "Yellow",
            >= 5 => "Orange",
            >= 3 => "LightRed",
            _ => "Red",
        };
        return new MapCell('@', Color(color), Black, "player", TileLighting.Lit, null, under.TileKey, under.Lighting);
    }

    public MapCell Monster(MonsterRaceDef race, MapCell under) =>
        new(race.Glyph, Color(PurpleUniques && race.IsUnique ? "Violet" : race.Color), Black, "monster:" + race.Id, TileLighting.Lit,
            "monster-name:" + race.Name.ToLowerInvariant(), under.TileKey, under.Lighting);

    /// <summary>An object of this kind, unidentified by any flavour (what a hallucinating player "sees").</summary>
    public MapCell ObjectKind(ObjectKindDef kind, ObjectBaseDef objectBase, MapCell under)
    {
        var fg = Color(objectBase.Color);
        if (under.Lighting == TileLighting.Dark) fg = Dim(fg);
        return new MapCell(objectBase.Glyph, fg, Black, "object:" + kind.Id, under.Lighting, "object-base:" + objectBase.Id,
            under.TileKey, under.Lighting);
    }

    public MapCell Trap(TrapDef trap, MapCell under)
    {
        var fg = under.Lighting == TileLighting.Dark ? Dim(Color(trap.Color)) : Color(trap.Color);
        return new MapCell(trap.Glyph, fg, Black, "trap:" + trap.Id, under.Lighting, null, under.TileKey, under.Lighting);
    }

    /// <summary>
    /// A floor object (or a pile, shown as <c>&amp;</c> as in Angband). Flavoured objects use their
    /// flavour's colour and tile; tilesets fall back to a per-base tile.
    /// </summary>
    public MapCell Object(Angband.Core.Items.Item top, int pileSize, Angband.Core.Items.PlayerKnowledge knowledge, MapCell under)
    {
        if (pileSize > 1)
            return new MapCell('&', Color("White"), Black, "object:pile", under.Lighting, "object-base:" + top.Base.Id,
                under.TileKey, under.Lighting);

        var flavor = top.IsFlavored ? knowledge.Flavor(top.Kind) : null;
        var fg = Color(flavor?.Color ?? top.Base.Color);
        if (under.Lighting == TileLighting.Dark) fg = Dim(fg);
        var key = flavor is not null && top.Base.Flavor != "scroll"
            ? $"flavor:{top.Base.Flavor}:{flavor.Name.ToLowerInvariant()}"
            : "object:" + top.Kind.Id;
        return new MapCell(top.Base.Glyph, fg, Black, key, under.Lighting, "object-base:" + top.Base.Id,
            under.TileKey, under.Lighting);
    }

    public uint Color(string name) =>
        ColorBlind && ColorBlindPalette.TryGetValue(name, out var safe) ? safe
        : _colors.TryGetValue(name, out var c) ? c : 0xFFFF00FF;

    /// <summary>Use <see cref="ColorBlindPalette"/> for the colours red–green colour blindness confuses.</summary>
    public bool ColorBlind { get; set; }

    /// <summary>
    /// Colour-blind friendly replacements (after the Okabe–Ito palette): greens lean blue, reds lean
    /// vermilion and orange, and blues, yellows and purples are set well apart, so health (green to
    /// red), elements and monster colours stay distinct for red–green colour blindness.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, uint> ColorBlindPalette = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["Red"] = 0xFFD55E00,
        ["LightRed"] = 0xFFFF9966,
        ["Orange"] = 0xFFE69F00,
        ["Yellow"] = 0xFFF0E442,
        ["LightYellow"] = 0xFFF8F0A8,
        ["Green"] = 0xFF009E73,
        ["LightGreen"] = 0xFF5CE6C8,
        ["Teal"] = 0xFF00A6C8,
        ["LightTeal"] = 0xFF7FDCEB,
        ["Blue"] = 0xFF0072B2,
        ["LightBlue"] = 0xFF56B4E9,
        ["DeepLightBlue"] = 0xFF3A9BE0,
        ["Violet"] = 0xFFCC79A7,
        ["LightViolet"] = 0xFFE3B5D0,
        ["Purple"] = 0xFF9E4F8C,
        ["LightPurple"] = 0xFFE08FD0,
        ["Magenta"] = 0xFFE36FB0,
        ["LightPink"] = 0xFFF5B8C8,
    };

    public static uint Dim(uint argb)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        return 0xFF000000 | (r * 5 / 10 << 16) | (g * 5 / 10 << 8) | (b * 5 / 10);
    }

    private static uint ParseColor(string hex) =>
        0xFF000000 | uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
