namespace Angband.Core.World;

/// <summary>Per-square state that is not a property of the terrain type.</summary>
[Flags]
public enum SquareFlags : ushort
{
    None = 0,
    /// <summary>Known to the player on arrival (mapped labyrinths, the town by day).</summary>
    Mark = 1 << 0,
    /// <summary>Permanently lit.</summary>
    Glow = 1 << 1,
    /// <summary>Part of a vault.</summary>
    Vault = 1 << 2,
    /// <summary>Part of a room (floor or wall).</summary>
    Room = 1 << 3,
    /// <summary>Wall inside a room structure.</summary>
    WallInner = 1 << 4,
    /// <summary>Outer wall of a room; tunnels may pierce it.</summary>
    WallOuter = 1 << 5,
    /// <summary>Wall tunnels must not pierce.</summary>
    WallSolid = 1 << 6,
    /// <summary>Currently in view of the player.</summary>
    View = 1 << 7,
    /// <summary>Currently seen (in view and lit).</summary>
    Seen = 1 << 8,
    /// <summary>A trap here has been noticed.</summary>
    TrapVisible = 1 << 9,
    /// <summary>No staircases may be allocated here.</summary>
    NoStairs = 1 << 10,
    /// <summary>No random traps may be allocated here.</summary>
    NoTrap = 1 << 11,
    /// <summary>Lit this turn by a carried light source (the player's torch, later glowing monsters).</summary>
    Lit = 1 << 12,
    /// <summary>No teleporting here (Angband SQUARE_NO_TELEPORT: a gauntlet's arrival cavern and maze).</summary>
    NoTeleport = 1 << 13,
    /// <summary>Magic mapping and detection pass it by (Angband SQUARE_NO_MAP: a gauntlet's maze).</summary>
    NoMap = 1 << 14,
    /// <summary>No random monsters are placed here (Angband SQUARE_MON_RESTRICT; generation only).</summary>
    MonRestrict = 1 << 15,

    AnyWallMarker = WallInner | WallOuter | WallSolid,
}
