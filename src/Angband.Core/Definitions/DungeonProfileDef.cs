namespace Angband.Core.Definitions;

/// <summary>
/// A level "profile" (Angband dungeon_profile.txt): which generator to run and how to tune it.
/// Several profiles can share a generator to give levels distinct themes.
/// </summary>
public sealed class DungeonProfileDef
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Generator algorithm: <c>town</c>, <c>classic</c>, <c>cavern</c> or <c>labyrinth</c>.</summary>
    public required string Generator { get; init; }
    public int MinDepth { get; init; } = 1;
    public int MaxDepth { get; init; } = 127;
    /// <summary>Relative selection weight among eligible profiles.</summary>
    public int Weight { get; init; } = 1;

    public int Width { get; init; } = 198;
    public int Height { get; init; } = 66;

    /// <summary>Room allocation block size (classic generator).</summary>
    public int BlockSize { get; init; } = 11;
    /// <summary>Number of room placement attempts (classic generator).</summary>
    public int RoomAttempts { get; init; } = 50;
    /// <summary>Rooms are lit when <c>depth &lt;= randint1(RoomLightDepth)</c>.</summary>
    public int RoomLightDepth { get; init; } = 25;

    public TunnelParams Tunnel { get; init; } = new();
    public StreamerParams Streamers { get; init; } = new();
    public DoorParams Doors { get; init; } = new();
    public AllocationParams Allocation { get; init; } = new();
    public CavernParams Cavern { get; init; } = new();
    public LabyrinthParams Labyrinth { get; init; } = new();
    public IReadOnlyList<RoomTypeWeight> Rooms { get; init; } = [];
}

public sealed class RoomTypeWeight
{
    /// <summary>Room builder id: simple, overlap, crossed, large, circular, template, lesser_vault, ...</summary>
    public required string Type { get; init; }
    public int Weight { get; init; } = 1;
    public int MinDepth { get; init; }
    public int MaxDepth { get; init; } = 127;
    /// <summary>Maximum number of rooms of this type per level (0 = unlimited).</summary>
    public int MaxCount { get; init; }
}

public sealed class TunnelParams
{
    /// <summary>% chance of a random direction when changing direction.</summary>
    public int Random { get; init; } = 10;
    /// <summary>% chance of re-evaluating direction each step.</summary>
    public int Change { get; init; } = 30;
    /// <summary>% chance of continuing past an existing corridor (else may stop at the junction).</summary>
    public int Continue { get; init; } = 15;
    /// <summary>% chance of a door where a tunnel pierces a room wall.</summary>
    public int PierceDoor { get; init; } = 25;
    /// <summary>% chance of a door beside a tunnel junction.</summary>
    public int JunctionDoor { get; init; } = 90;
}

public sealed class StreamerParams
{
    public int Density { get; init; } = 5;
    public int Range { get; init; } = 2;
    public int MagmaCount { get; init; } = 3;
    /// <summary>1-in-N chance for a magma square to hold treasure.</summary>
    public int MagmaTreasure { get; init; } = 90;
    public int QuartzCount { get; init; } = 2;
    public int QuartzTreasure { get; init; } = 40;
}

public sealed class DoorParams
{
    public int OpenWeight { get; init; } = 30;
    public int BrokenWeight { get; init; } = 10;
    public int SecretWeight { get; init; } = 20;
    public int ClosedWeight { get; init; } = 40;
    /// <summary>% of closed doors that are locked.</summary>
    public int LockedChance { get; init; } = 25;
    public int MaxLockPower { get; init; } = 7;
}

public sealed class AllocationParams
{
    public string UpStairs { get; init; } = "1d2";
    public string DownStairs { get; init; } = "1d2+1";
    public string Rubble { get; init; } = "1d5";
    /// <summary>Traps: <c>randint1(TrapBase + depth / TrapDepthDivisor)</c>.</summary>
    public int TrapBase { get; init; } = 3;
    public int TrapDepthDivisor { get; init; } = 4;
    public int MonsterMin { get; init; } = 14;
    public int MonsterRandom { get; init; } = 8;
    public int RoomObjects { get; init; } = 9;
    public int AnywhereObjects { get; init; } = 3;
    public int Gold { get; init; } = 3;
}

public sealed class CavernParams
{
    public int MinHeight { get; init; } = 40;
    public int MaxHeight { get; init; } = 66;
    public int MinWidth { get; init; } = 90;
    public int MaxWidth { get; init; } = 198;
    /// <summary>Initial % of wall squares before smoothing.</summary>
    public int FillPercent { get; init; } = 44;
    public int SmoothingPasses { get; init; } = 5;
    /// <summary>Open pockets smaller than this are filled in instead of joined.</summary>
    public int MinPocketSize { get; init; } = 20;
    /// <summary>Minimum % of the level that must be open floor.</summary>
    public int MinOpenPercent { get; init; } = 20;
}

public sealed class LabyrinthParams
{
    public int BaseHeight { get; init; } = 15;
    public int BaseWidth { get; init; } = 51;
    /// <summary>Levels grow by this many depth units per size step.</summary>
    public int GrowthDepth { get; init; } = 15;
    /// <summary>% of passages given a door.</summary>
    public int DoorChance { get; init; } = 10;
}
