using Angband.Core.Geometry;

namespace Angband.Core.World;

public enum SpawnKind
{
    Monster,
    Object,
    GoodObject,
    GreatObject,
    Gold,
    /// <summary>Either a monster or an object, decided when populating.</summary>
    MonsterOrObject,
    /// <summary>A member of a monster pit (orderly ranks of similar monsters).</summary>
    PitMonster,
    /// <summary>A member of a monster nest (a jumble of similar monsters).</summary>
    NestMonster,
}

/// <summary>
/// A request left by the level generator for the population step (monsters/objects are placed by
/// later systems). <see cref="DepthBonus"/> is the "out of depth" allowance, as in Angband vaults.
/// </summary>
public readonly record struct SpawnHint(Loc Loc, SpawnKind Kind, int DepthBonus = 0, string? Tag = null);

/// <summary>Counts of randomly placed things a level should receive when populated.</summary>
public readonly record struct PopulationBudget(int Monsters, int RoomObjects, int AnywhereObjects, int Gold);
