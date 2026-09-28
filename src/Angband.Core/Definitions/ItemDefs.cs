using Angband.Core.Randomness;

namespace Angband.Core.Definitions;

/// <summary>Equipment slots (Angband 4.2's humanoid body).</summary>
public enum EquipSlot
{
    None,
    Weapon,
    Bow,
    Ring,
    Amulet,
    Light,
    Body,
    Cloak,
    Shield,
    Head,
    Hands,
    Feet,
}

/// <summary>A broad class of objects (Angband object_base.txt / tval): sword, potion, arrow...</summary>
public sealed class ObjectBaseDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public char Glyph { get; init; } = '?';
    public string Color { get; init; } = "White";
    public EquipSlot Slot { get; init; }
    public int MaxStack { get; init; } = 40;
    /// <summary>Elements that can destroy (or, for weapons and armour, damage) things of this base (Angband HATES_*).</summary>
    public IReadOnlyList<string> Hates { get; init; } = [];
    /// <summary>% chance to break when thrown or fired and it hits.</summary>
    public int BreakChance { get; init; } = 10;
    /// <summary>Flavour group ("potion", "scroll", "ring"...) when kinds of this base look alike until learned.</summary>
    public string? Flavor { get; init; }
    /// <summary>Missile fired by this launcher kind ("shot", "arrow", "bolt"), or the ammo class of this base.</summary>
    public string? AmmoClass { get; init; }
    public bool IsAmmo { get; init; }
    public bool IsWeapon => Slot == EquipSlot.Weapon;
    public bool IsWearable => Slot != EquipSlot.None;
}

/// <summary>A property an object can have, learned once per game (4.2 "runes").</summary>
public static class RuneIds
{
    public const string ToHit = "to_h";
    public const string ToDam = "to_d";
    public const string ToAc = "to_a";
    public static string Slay(string monsterFlag) => "slay:" + monsterFlag;
    public static string Brand(string element) => "brand:" + element;
    public static string Resist(string element) => "resist:" + element;
    public static string Modifier(string mod) => "mod:" + mod;
    public static string Curse(string curse) => "curse:" + curse;
    public static string Flag(string flag) => "flag:" + flag;
}

/// <summary>Object kind flags that grant abilities while worn (each is also a rune).</summary>
public static class ItemFlags
{
    public const string Regen = "REGEN";
    public const string SlowDigest = "SLOW_DIGEST";
    public const string HoldLife = "HOLD_LIFE";
    /// <summary>Sense the minds of monsters nearby (Angband TELEPATHY / ESP).</summary>
    public const string Telepathy = "TELEPATHY";

    public static readonly IReadOnlySet<string> Abilities = new HashSet<string>(StringComparer.Ordinal) { Regen, SlowDigest, HoldLife, Telepathy };

    public static string Name(string flag) => flag switch
    {
        Regen => "regeneration",
        SlowDigest => "slow digestion",
        HoldLife => "hold life",
        Telepathy => "telepathy",
        _ => flag.ToLowerInvariant().Replace('_', ' '),
    };
}

/// <summary>Numeric properties objects can modify (Angband "values").</summary>
public static class ItemModifiers
{
    public const string Speed = "speed";
    public const string Light = "light";
    public const string Stealth = "stealth";
    public const string Blows = "blows";
    public const string Shots = "shots";
    public const string Infravision = "infra";
    public const string Strength = "str";
    public const string Intelligence = "int";
    public const string Wisdom = "wis";
    public const string Dexterity = "dex";
    public const string Constitution = "con";
    public const string Searching = "search";
    /// <summary>Digging power (Angband TUNNEL): +20 digging skill per point.</summary>
    public const string Tunnel = "tunnel";
}

public sealed class SlayDef
{
    public required string MonsterFlag { get; init; }
    public int Multiplier { get; init; } = 2;
    public string Verb { get; init; } = "smite";
    public string Name { get; init; } = "";
}

public sealed class BrandDef
{
    public required string Element { get; init; }
    public int Multiplier { get; init; } = 3;
    public string Verb { get; init; } = "burn";
    public string Name { get; init; } = "";
}

/// <summary>A particular object (Angband object.txt): Dagger, Potion of Cure Light Wounds...</summary>
public sealed class ObjectKindDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Base { get; init; }
    /// <summary>Elements this kind is proof against, though its base hates them (Angband IGNORE_*).</summary>
    public IReadOnlyList<string> Ignore { get; init; } = [];
    public int Level { get; init; }
    /// <summary>Relative commonness; 0 means never randomly generated.</summary>
    public int Commonness { get; init; } = 10;
    public int MinDepth { get; init; }
    public int MaxDepth { get; init; } = 127;
    public int Cost { get; init; }
    /// <summary>In tenths of a pound, as in Angband.</summary>
    public int Weight { get; init; }
    public Dice Damage { get; init; } = Dice.Zero;
    public int Armour { get; init; }
    public int ToHit { get; init; }
    public int ToDam { get; init; }
    public int ToAc { get; init; }
    /// <summary>Launcher damage multiplier.</summary>
    public int Multiplier { get; init; }
    /// <summary>Light radius, speed, stealth... given by the kind itself.</summary>
    public IReadOnlyDictionary<string, int> Modifiers { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> Resists { get; init; } = [];
    public IReadOnlyList<SlayDef> Slays { get; init; } = [];
    /// <summary>Brands (e.g. a flask of oil burns what it hits).</summary>
    public IReadOnlyList<BrandDef> Brands { get; init; } = [];
    /// <summary>Curses every object of this kind carries.</summary>
    public IReadOnlyList<string> Curses { get; init; } = [];
    public IReadOnlyList<string> Flags { get; init; } = [];
    /// <summary>What using it does, e.g. <c>heal:20; cure:blind</c>. See <see cref="Items.ItemEffects"/>.</summary>
    public string? Effect { get; init; }
    /// <summary>Starting fuel for light sources, in player turns (0 = needs none).</summary>
    public int Fuel { get; init; }
    /// <summary>Stack size when generated, e.g. <c>5d4</c> for arrows.</summary>
    public Dice StackSize { get; init; } = Dice.Constant(1);
    /// <summary>
    /// Values rolled when the object is made, in Angband's random-value syntax (<see cref="Items.RandomValue"/>):
    /// modifiers by id, plus <c>to_h</c>, <c>to_d</c> and <c>to_a</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Rolls { get; init; } = new Dictionary<string, string>();
    /// <summary>Wands and staffs: charges when made (random-value syntax).</summary>
    public string? Charges { get; init; }
    /// <summary>Rods and activatable gear: game turns / 10 to recharge after use (random-value syntax).</summary>
    public string? Recharge { get; init; }
    /// <summary>What the object does when activated (dragon armour breath, rings of Flames...): an effect string.</summary>
    public string? Activation { get; init; }
    /// <summary>Plain-English description of <see cref="Activation"/>.</summary>
    public string? ActivationText { get; init; }
    /// <summary>Power of the kind's effect or activation (Angband object.txt "power"), for pricing.</summary>
    public int Power { get; init; }
    public string Description { get; init; } = "";

    public bool Has(string flag) => Flags.Contains(flag);

    /// <summary>Made only as an artifact (Angband INSTA_ART: the Star, the rings of power); never flavoured.</summary>
    public bool IsSpecialArtifactKind => Has("INSTA_ART");
}

/// <summary>An "ego" enchantment family (Angband ego_item.txt): of Slay Evil, of Resist Fire...</summary>
public sealed class EgoItemDef
{
    public required string Id { get; init; }
    /// <summary>Elements the ego makes an item proof against (Angband IGNORE_*).</summary>
    public IReadOnlyList<string> Ignore { get; init; } = [];
    /// <summary>Suffix such as <c>of Flame</c>.</summary>
    public required string Name { get; init; }
    public IReadOnlyList<string> Bases { get; init; } = [];
    public int Level { get; init; }
    public int Commonness { get; init; } = 10;
    public int MaxDepth { get; init; } = 127;
    /// <summary>Extra rolled bonuses: <c>1d5</c> etc., added on top of normal magic.</summary>
    public Dice ToHit { get; init; } = Dice.Zero;
    public Dice ToDam { get; init; } = Dice.Zero;
    public Dice ToAc { get; init; } = Dice.Zero;
    public IReadOnlyDictionary<string, int> Modifiers { get; init; } = new Dictionary<string, int>();
    /// <summary>Random modifiers (Angband random-value syntax), rolled when the ego is applied.</summary>
    public IReadOnlyDictionary<string, string> Rolls { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<SlayDef> Slays { get; init; } = [];
    public IReadOnlyList<BrandDef> Brands { get; init; } = [];
    public IReadOnlyList<string> Resists { get; init; } = [];
    /// <summary>Ability flags (see <see cref="ItemFlags"/>).</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];
    /// <summary>Curses the ego always carries (e.g. a "Morgul" weapon).</summary>
    public IReadOnlyList<string> Curses { get; init; } = [];
}

/// <summary>A unique named object (Angband artifact.txt). Each appears at most once per game.</summary>
public sealed class ArtifactDef
{
    public required string Id { get; init; }
    /// <summary>Full name suffix, e.g. <c>'Narthanc'</c> or <c>of Galadriel</c>.</summary>
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public int Level { get; init; }
    /// <summary>1-in-N chance once eligible.</summary>
    public int Rarity { get; init; } = 10;
    public int ToHit { get; init; }
    public int ToDam { get; init; }
    public int ToAc { get; init; }
    public int Armour { get; init; }
    public Dice? Damage { get; init; }
    public IReadOnlyDictionary<string, int> Modifiers { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<SlayDef> Slays { get; init; } = [];
    public IReadOnlyList<BrandDef> Brands { get; init; } = [];
    public IReadOnlyList<string> Resists { get; init; } = [];
    /// <summary>Ability flags (see <see cref="ItemFlags"/>).</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];
    public IReadOnlyList<string> Curses { get; init; } = [];
    /// <summary>Activation effect string (Angband artifact "act"), with its description and recharge time.</summary>
    public string? Activation { get; init; }
    public string? ActivationText { get; init; }
    public string? Recharge { get; init; }
    /// <summary>Power of the activation (Angband activation.txt), for pricing and random artifacts.</summary>
    public int ActivationPower { get; init; }
    public string Description { get; init; } = "";
}

/// <summary>
/// A curse (Angband curse.txt). In 4.2 cursed items can be removed freely; curses instead carry
/// penalties and occasional bad effects, and are learned like other runes.
/// </summary>
public sealed class CurseDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public int ToHit { get; init; }
    public int ToDam { get; init; }
    public int ToAc { get; init; }
    public IReadOnlyDictionary<string, int> Modifiers { get; init; } = new Dictionary<string, int>();
    /// <summary>Resistances this curse takes away (sets vulnerability).</summary>
    public IReadOnlyList<string> Vulnerabilities { get; init; } = [];
    /// <summary>1-in-N chance per player turn of <see cref="Effect"/> firing.</summary>
    public int EffectChance { get; init; }
    /// <summary>Effect string, same syntax as object effects (e.g. <c>teleport:40</c>, <c>timed:poisoned:10</c>).</summary>
    public string? Effect { get; init; }
    public string EffectMessage { get; init; } = "";
    /// <summary>Which object bases can carry it (empty = any wearable).</summary>
    public IReadOnlyList<string> Bases { get; init; } = [];
}

/// <summary>Unknown-object appearances for one flavour group (potion colours, ring stones...).</summary>
public sealed class FlavorGroupDef
{
    public required string Id { get; init; }
    /// <summary>How an unknown object is named: <c>{flavor} Potion</c>.</summary>
    public string Pattern { get; init; } = "{flavor} {base}";
    public IReadOnlyList<FlavorDef> Flavors { get; init; } = [];
    /// <summary>Random titles are built from these syllables instead (scrolls).</summary>
    public IReadOnlyList<string> Syllables { get; init; } = [];
}

public sealed class FlavorDef
{
    public required string Name { get; init; }
    public string Color { get; init; } = "White";
}

/// <summary>
/// A chest trap (Angband chest_trap.txt). The list order gives each trap its bit in a chest's trap
/// value: the first entry ("locked") is 1, the next 2, then 4... — and those numbers are also how hard
/// the chest is to open or disarm.
/// </summary>
public sealed class ChestTrapDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Lowest chest level that can have it.</summary>
    public int Level { get; init; } = 1;
    /// <summary>Effect string (see the trap effects in GameSession.Traps.cs); empty for a plain lock.</summary>
    public string Effect { get; init; } = "";
    public string Message { get; init; } = "";
    /// <summary>What killed the player, if it did.</summary>
    public string DeathMessage { get; init; } = "a chest trap";
    /// <summary>Magical (runes) rather than mechanical.</summary>
    public bool Magic { get; init; }
    /// <summary>Destroys the chest's contents when it goes off.</summary>
    public bool Destroy { get; init; }
    /// <summary>This trap's bit in a chest's trap value (set from the list order).</summary>
    public int Bit { get; internal set; }
}
