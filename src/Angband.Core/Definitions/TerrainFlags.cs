namespace Angband.Core.Definitions;

/// <summary>Terrain properties (a subset of Angband's terrain.txt flags).</summary>
[Flags]
public enum TerrainFlags : uint
{
    None = 0,
    /// <summary>Allows line of sight.</summary>
    Los = 1u << 0,
    /// <summary>Allows projections (bolts, balls) to pass.</summary>
    Project = 1u << 1,
    /// <summary>Can be walked on.</summary>
    Passable = 1u << 2,
    /// <summary>Worth noticing (stops running, shown by detection).</summary>
    Interesting = 1u << 3,
    /// <summary>Cannot be destroyed or tunnelled.</summary>
    Permanent = 1u << 4,
    /// <summary>Easily passable open ground.</summary>
    Easy = 1u << 5,
    /// <summary>Can hold a trap.</summary>
    Trap = 1u << 6,
    /// <summary>Can hold objects.</summary>
    Object = 1u << 7,
    /// <summary>Is lit by the player's light source.</summary>
    Torch = 1u << 8,
    Floor = 1u << 9,
    Wall = 1u << 10,
    Rock = 1u << 11,
    Granite = 1u << 12,
    /// <summary>Any kind of door, including secret doors.</summary>
    DoorAny = 1u << 13,
    DoorClosed = 1u << 14,
    Stair = 1u << 15,
    UpStair = 1u << 16,
    DownStair = 1u << 17,
    Shop = 1u << 18,
    Rubble = 1u << 19,
    Magma = 1u << 20,
    Quartz = 1u << 21,
    /// <summary>Contains treasure (mineral vein with treasure).</summary>
    Gold = 1u << 22,
    Fiery = 1u << 23,
    /// <summary>Glows by itself (lava).</summary>
    Bright = 1u << 24,
    /// <summary>Scent does not linger (used by monster tracking).</summary>
    NoScent = 1u << 25,
    /// <summary>Sound/flow does not pass (used by monster tracking).</summary>
    NoFlow = 1u << 26,
    /// <summary>Appears as something else until discovered (secret doors).</summary>
    Secret = 1u << 27,
    /// <summary>An open door that can be closed.</summary>
    Closable = 1u << 28,
}
