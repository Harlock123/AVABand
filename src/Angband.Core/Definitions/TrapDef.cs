namespace Angband.Core.Definitions;

public sealed class TrapDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public char Glyph { get; init; } = '^';
    public string Color { get; init; } = "White";
    public int MinDepth { get; init; }
    public int MaxDepth { get; init; } = 127;
    /// <summary>Relative allocation weight among eligible traps.</summary>
    public int Weight { get; init; } = 1;
    /// <summary>Effect identifier resolved by the effects system (e.g. <c>teleport</c>, <c>damage:fire:4d6</c>).</summary>
    public string Effect { get; init; } = "";
    /// <summary>Trap doors cannot appear where the player could not fall (e.g. the bottom level).</summary>
    public bool IsTrapDoor { get; init; }
    /// <summary>A glyph of warding (Angband rune of protection): harmless to the player, blocks monsters.</summary>
    public bool Warding { get; init; }
    /// <summary>A spider web (Angband's web trap): blocks until cleared; nothing is set off.</summary>
    public bool Web { get; init; }
    public string Description { get; init; } = "";
    /// <summary>How hard it is to disarm (Angband trap power); 0 means 5 + twice its minimum depth.</summary>
    public int Power { get; init; }
    public int DisarmPower => Power > 0 ? Power : 5 + 2 * MinDepth;
    /// <summary>A magical rune (disarmed with the magic side of the disarm skill).</summary>
    public bool IsRune => Id.Contains("rune") || Effect.StartsWith("summon") || Effect.StartsWith("teleport");

    /// <summary>1-based index stored in squares (0 = no trap).</summary>
    public ushort Index { get; internal set; }
}
