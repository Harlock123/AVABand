namespace Angband.Core.Definitions;

/// <summary>
/// A quest (Angband quest.txt): kill <see cref="Number"/> of a monster race on dungeon level
/// <see cref="Level"/>. Until it is done that level has no way down; finishing the last quest wins.
/// </summary>
public sealed class QuestDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Level { get; init; }
    /// <summary>Monster race id.</summary>
    public required string Race { get; init; }
    public int Number { get; init; } = 1;
}
