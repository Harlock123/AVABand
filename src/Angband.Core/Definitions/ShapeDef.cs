namespace Angband.Core.Definitions;

/// <summary>
/// A form the player can take (Angband shape.txt): bonuses and protections while in it, what the
/// change costs, and how its attacks are described. Items and spells can't be used in a shape.
/// </summary>
public sealed class ShapeDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int ToHit { get; init; }
    public int ToDam { get; init; }
    public int ToAc { get; init; }
    public IReadOnlyDictionary<string, int> Skills { get; init; } = new Dictionary<string, int>();
    /// <summary>Stat and other modifiers (as on equipment).</summary>
    public IReadOnlyDictionary<string, int> Modifiers { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> Resists { get; init; } = [];
    /// <summary>Elements the shape makes you immune to (Angband RES_x[3]: the Púkel-man and poison).</summary>
    public IReadOnlyList<string> Immunities { get; init; } = [];
    public IReadOnlyList<string> Flags { get; init; } = [];
    /// <summary>Applied on changing into it (an effect string).</summary>
    public string Effect { get; init; } = "";
    /// <summary>Attack verbs ("bite", "claw"...).</summary>
    public IReadOnlyList<string> Blows { get; init; } = [];
}
