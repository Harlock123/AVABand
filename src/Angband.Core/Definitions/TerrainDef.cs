namespace Angband.Core.Definitions;

/// <summary>One terrain feature (floor, wall, door, stair, shop entrance...).</summary>
public sealed class TerrainDef
{
    /// <summary>Stable textual identifier, e.g. <c>granite_wall</c>. Referenced by code and tilesets.</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public char Glyph { get; init; } = '?';
    /// <summary>Named color from the palette (Angband colour names).</summary>
    public string Color { get; init; } = "White";
    public TerrainFlags Flags { get; init; }
    /// <summary>Map display priority when zoomed out (higher wins).</summary>
    public int Priority { get; init; }
    /// <summary>Terrain this one looks like until discovered (secret doors look like granite).</summary>
    public string? Mimic { get; init; }
    /// <summary>For shop entrances: the shop id (general, armoury, ...).</summary>
    public string? Shop { get; init; }
    /// <summary>Difficulty to tunnel through; 0 means not diggable.</summary>
    public int DigDifficulty { get; init; }
    public string Description { get; init; } = "";

    /// <summary>Dense index assigned by the registry; stored in each square.</summary>
    public ushort Index { get; internal set; }

    public bool Has(TerrainFlags flag) => (Flags & flag) == flag;
    public bool HasAny(TerrainFlags flags) => (Flags & flags) != 0;

    public override string ToString() => Id;
}
