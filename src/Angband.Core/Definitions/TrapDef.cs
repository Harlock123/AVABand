using Angband.Core.Randomness;

namespace Angband.Core.Definitions;

/// <summary>A kind of trap (Angband trap.txt): floor traps, runes, glyphs of warding, webs.</summary>
public sealed class TrapDef
{
    public required string Id { get; init; }
    /// <summary>What it looks like (Angband's first name: "strange rune", "dart trap").</summary>
    public required string Name { get; init; }
    public char Glyph { get; init; } = '^';
    public string Color { get; init; } = "White";
    public int MinDepth { get; init; }
    public int MaxDepth { get; init; } = 127;
    /// <summary>Angband appear rarity: a trap is picked with weight 100 / rarity; 0 is never placed at random.</summary>
    public int Rarity { get; init; }
    /// <summary>
    /// Angband trap flags: TRAP (a player trap), FLOOR, MAGICAL (a rune, disarmed by magic),
    /// ONETIME (gone once sprung), SAVE_ARMOR, SAVE_THROW, PIT, DOWN (a trap door), DELAY (goes off
    /// as you leave), VISIBLE, GLYPH, WEB.
    /// </summary>
    public IReadOnlyList<string> Flags { get; init; } = [];
    /// <summary>Gear flags that save you from it (Angband save: FEATHER for pits and trap doors).</summary>
    public IReadOnlyList<string> Save { get; init; } = [];
    /// <summary>How hard it is to notice (Angband visibility, a random value): seen only by a search skill at least this.</summary>
    public string Visibility { get; init; } = "0";
    /// <summary>What it does: a trap effect string (see <c>GameSession.ApplyTrapEffects</c>).</summary>
    public string Effect { get; init; } = "";
    /// <summary>What it may also do, one time in two (Angband effect-xtra).</summary>
    public string? Extra { get; init; }
    public string? Message { get; init; }
    public string? MessageGood { get; init; }
    public string? MessageBad { get; init; }
    public string? MessageExtra { get; init; }
    public string Description { get; init; } = "";

    public bool Has(string flag) => Flags.Contains(flag);

    /// <summary>A player trap: one that goes off (not a glyph or a web).</summary>
    public bool IsTrap => Has("TRAP");
    /// <summary>Drops you to the next level (Angband DOWN).</summary>
    public bool IsTrapDoor => Has("DOWN");
    /// <summary>A glyph of warding (Angband GLYPH): harmless to the player, blocks monsters.</summary>
    public bool Warding => Has("GLYPH");
    /// <summary>A spider web (Angband WEB): blocks until cleared; nothing is set off.</summary>
    public bool Web => Has("WEB");
    /// <summary>A magical rune (Angband MAGICAL): disarmed with the magical disarming skill.</summary>
    public bool IsRune => Has("MAGICAL");

    /// <summary>Angband pick_trap: its weight among the traps allowed at a depth.</summary>
    public int Weight => IsTrap && Rarity > 0 ? 100 / Rarity : 0;

    /// <summary>Angband place_trap: the power to notice this one, rolled at the trap's level.</summary>
    public byte RollPower(GameRandom rng, int depth) =>
        (byte)Math.Clamp(Items.RandomValue.Parse(Visibility).Roll(rng, depth), 0, 255);

    /// <summary>1-based index stored in squares (0 = no trap).</summary>
    public ushort Index { get; internal set; }
}
