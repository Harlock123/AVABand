namespace Angband.Core.World;

/// <summary>A single dungeon grid. Kept small and blittable for fast copies and saves.</summary>
public struct Square
{
    /// <summary>Terrain index into <see cref="Definitions.TerrainRegistry"/>.</summary>
    public ushort Feature;
    public SquareFlags Flags;
    /// <summary>1-based trap index, 0 = no trap.</summary>
    public ushort Trap;
    /// <summary>Lock power for locked doors (0 = unlocked).</summary>
    public byte LockPower;
    /// <summary>Slot id of the monster standing here, 0 = none (see <see cref="Monsters.MonsterRoster"/>).</summary>
    public ushort Monster;

    public readonly bool Has(SquareFlags flag) => (Flags & flag) == flag;
    public readonly bool HasAny(SquareFlags flags) => (Flags & flags) != 0;
}
