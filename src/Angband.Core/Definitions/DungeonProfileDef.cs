namespace Angband.Core.Definitions;

/// <summary>
/// A kind of level (Angband dungeon_profile.txt): which builder makes it (by its name: classic,
/// modified, moria, lair, gauntlet, hard centre, cavern, labyrinth, town) and how it is tuned.
/// </summary>
public sealed class DungeonProfileDef
{
    public required string Id { get; init; }
    /// <summary>The builder's name, as 4.2.5's list-dun-profiles.h has it.</summary>
    public required string Name { get; init; }
    public ProfileParams Params { get; init; } = new();
    public TunnelParams Tunnel { get; init; } = new();
    public StreamerParams Streamers { get; init; } = new();
    /// <summary>
    /// Its share of the levels it may appear on (Angband <c>alloc</c>); -1 for those chosen only
    /// by generate.c's own tests (labyrinths, moria levels, the town), 0 never.
    /// </summary>
    public int Alloc { get; init; }
    /// <summary>The shallowest level it can appear on (Angband <c>min-level</c>).</summary>
    public int MinLevel { get; init; }
    /// <summary>The rooms it builds, in the order they are tried (Angband <c>room</c> lines).</summary>
    public IReadOnlyList<RoomProfileDef> Rooms { get; init; } = [];
}

/// <summary>Angband dungeon_profile.txt <c>params</c>: block size, rooms to aim for, unusualness, most rarity.</summary>
public sealed class ProfileParams
{
    public int BlockSize { get; init; } = 1;
    public int Rooms { get; init; }
    /// <summary>The higher, the rarer the rarer rooms (Angband dun_unusual).</summary>
    public int Unusual { get; init; } = 200;
    public int MaxRarity { get; init; }
}

/// <summary>Angband dungeon_profile.txt <c>tunnel</c>: percentage chances while digging corridors.</summary>
public sealed class TunnelParams
{
    /// <summary>Of a random direction when turning (rnd).</summary>
    public int Random { get; init; } = 10;
    /// <summary>Of turning at any step (chg).</summary>
    public int Change { get; init; } = 30;
    /// <summary>Of going on through a corridor it meets rather than perhaps stopping (con).</summary>
    public int Continue { get; init; } = 15;
    /// <summary>Of a door where a tunnel pierces a room's wall (pen).</summary>
    public int PierceDoor { get; init; } = 25;
    /// <summary>Of a door beside a junction of tunnels (jct).</summary>
    public int JunctionDoor { get; init; } = 50;
}

/// <summary>Angband dungeon_profile.txt <c>streamer</c>: mineral veins.</summary>
public sealed class StreamerParams
{
    /// <summary>Grids turned about each step of the walk (den).</summary>
    public int Density { get; init; } = 5;
    /// <summary>How far from the walk (rng).</summary>
    public int Range { get; init; } = 2;
    /// <summary>Magma streamers (mag), one in <see cref="MagmaTreasure"/> of their grids with treasure (mc).</summary>
    public int Magma { get; init; } = 3;
    public int MagmaTreasure { get; init; } = 90;
    /// <summary>Quartz streamers (qua), one in <see cref="QuartzTreasure"/> with treasure (qc).</summary>
    public int Quartz { get; init; } = 2;
    public int QuartzTreasure { get; init; } = 40;
}

/// <summary>
/// A room a profile builds (Angband dungeon_profile.txt <c>room</c>): the builder (by name), the
/// room template rating it asks for, the space it reserves, its least depth, whether it is a pit
/// or nest, its rarity and the cutoff that picks it.
/// </summary>
public sealed class RoomProfileDef
{
    public required string Name { get; init; }
    public int Rating { get; init; }
    public int Height { get; init; }
    public int Width { get; init; }
    public int Level { get; init; }
    public bool Pit { get; init; }
    public int Rarity { get; init; }
    public int Cutoff { get; init; }
}
