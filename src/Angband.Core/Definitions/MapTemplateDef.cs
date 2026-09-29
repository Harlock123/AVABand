namespace Angband.Core.Definitions;

/// <summary>
/// A vault or interesting room (Angband vault.txt), kept in 4.2.5's own symbols: <c>%</c> the
/// outer wall tunnels may pierce, <c>#</c> granite, <c>@</c> permanent rock, <c>*</c> a treasure
/// vein, <c>:</c> rubble, <c>`</c> lava, <c>+</c> a secret door, <c>^</c> a trap, <c>&amp;</c>
/// treasure or a trap, <c>&lt; &gt;</c> stairs, <c>0-9</c> monsters and treasure more or less out
/// of depth, <c>~ $ ] | = " ! ? _ - ,</c> particular treasure, letters a monster of that symbol.
/// </summary>
public sealed class VaultDef
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>"Lesser vault", "Medium vault (new)", "Greater vault", "Interesting room"...</summary>
    public required string Type { get; init; }
    /// <summary>Added to the level's monster rating (the feeling).</summary>
    public int Rating { get; init; }
    public int MinDepth { get; init; }
    public int MaxDepth { get; init; }
    /// <summary>Room flags: <c>FEW_ENTRANCES</c> (tunnels use its marked entrances).</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];
    public required IReadOnlyList<string> Rows { get; init; }

    public int Height => Rows.Count;
    public int Width => Rows.Count == 0 ? 0 : Rows[0].Length;
    public bool Has(string flag) => Flags.Contains(flag);
}

/// <summary>
/// A room template (Angband room_template.txt), in 4.2.5's symbols: <c>%</c> outside wall,
/// <c>#</c> granite, <c>^</c> a trap, <c>+</c> a door, <c>1-6</c> where a secret door may be,
/// <c>x ( )</c> optional walls and doors, <c>8</c> treasure (or stairs) with guards, <c>9</c>
/// monsters and treasure about, <c>[</c> treasure of the template's kind.
/// </summary>
public sealed class RoomTemplateDef
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public int Type { get; init; } = 1;
    /// <summary>Which rating profiles ask for (1, 2 or 3).</summary>
    public int Rating { get; init; }
    /// <summary>How many numbered door positions to choose among.</summary>
    public int Doors { get; init; }
    /// <summary>The object base <c>[</c> treasure is drawn from; null for any.</summary>
    public string? Tval { get; init; }
    public IReadOnlyList<string> Flags { get; init; } = [];
    public required IReadOnlyList<string> Rows { get; init; }

    public int Height => Rows.Count;
    public int Width => Rows.Count == 0 ? 0 : Rows[0].Length;
    public bool Has(string flag) => Flags.Contains(flag);
}

/// <summary>
/// A theme for pits, nests and other gatherings (Angband pit.txt): the room type it is for
/// (1 pits, 2 nests, 3 others), how rare and how deep, the chance of objects, and what monsters
/// belong — bases, colours, required and forbidden flags and spells, innate frequency, banned races.
/// </summary>
public sealed class PitProfileDef
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public int Room { get; init; }
    public int Rarity { get; init; } = 1;
    public int AverageLevel { get; init; }
    public int ObjectRarity { get; init; }
    public IReadOnlyList<string> Colors { get; init; } = [];
    public IReadOnlyList<string> Bases { get; init; } = [];
    public IReadOnlyList<string> Flags { get; init; } = [];
    public IReadOnlyList<string> ForbiddenFlags { get; init; } = [];
    public IReadOnlyList<string> Spells { get; init; } = [];
    public IReadOnlyList<string> ForbiddenSpells { get; init; } = [];
    public int InnateFrequency { get; init; }
    public IReadOnlyList<string> ForbiddenMonsters { get; init; } = [];
}
