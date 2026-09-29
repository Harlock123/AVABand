using Angband.Core.Geometry;

namespace Angband.Core.World;

public enum SpawnKind
{
    Monster,
    Object,
    GoodObject,
    GreatObject,
    Gold,
    /// <summary>
    /// A particular race, chosen by the generator (pits and nests): <see cref="SpawnHint.Tag"/> is
    /// <c>race-id|sleep</c> or <c>race-id|awake</c>, perhaps with <c>,group</c>.
    /// </summary>
    Race,
}

/// <summary>
/// A request left by the level generator for the population step (monsters/objects are placed by
/// later systems). <see cref="DepthBonus"/> is the "out of depth" allowance, as in Angband vaults.
/// For a monster, <see cref="Tag"/> lists (comma-separated) <c>sleep</c> or <c>awake</c>,
/// <c>group</c> (with its escort), <c>base:X</c> (only monsters of that symbol), <c>uniques</c>
/// (uniques allowed, one time in five), <c>pit:id</c> (a pit.txt theme); for an object,
/// <c>tval:base</c> (only that kind of object).
/// </summary>
public readonly record struct SpawnHint(Loc Loc, SpawnKind Kind, int DepthBonus = 0, string? Tag = null);

/// <summary>Counts of randomly placed things a level should receive when populated.</summary>
public readonly record struct PopulationBudget(int Monsters, int RoomObjects, int AnywhereObjects, int Gold);
