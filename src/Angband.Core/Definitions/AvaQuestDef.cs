namespace Angband.Core.Definitions;

/// <summary>
/// One of AVABand's own quests (ava_quests.json): its words — the offer, what the journal says at
/// each stage, the ending — and when the inn offers it. What it does is the quest engine's
/// (Game/GameSession.AvaQuests*.cs), keyed by <see cref="Id"/>. Angband's own quests (Sauron and
/// Morgoth, quests.json) are separate and unchanged.
/// </summary>
public sealed class AvaQuestDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Character level from which the inn offers it (0: it isn't offered, it is found).</summary>
    public int MinLevel { get; init; }
    /// <summary>Who gives it, for the quest log (empty: the Prancing Pony if it is offered there, else found in the dungeon).</summary>
    public string Source { get; init; } = "";
    /// <summary>What the innkeeper says when offering it.</summary>
    public string Offer { get; init; } = "";
    /// <summary>The journal's words for each stage; {depth}, {feet} and the like are filled in.</summary>
    public IReadOnlyDictionary<string, string> Stages { get; init; } = new Dictionary<string, string>();
}
