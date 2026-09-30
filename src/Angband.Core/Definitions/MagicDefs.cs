namespace Angband.Core.Definitions;

/// <summary>A school of magic (Angband 4.2 realms): arcane, divine, nature...</summary>
public sealed class RealmDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Stat that powers it ("int" or "wis").</summary>
    public string Stat { get; init; } = "int";
    /// <summary>Verb for casting: cast, recite, invoke...</summary>
    public string Verb { get; init; } = "cast";
    /// <summary>What a spell is called: spell, prayer, charm...</summary>
    public string SpellNoun { get; init; } = "spell";
    /// <summary>Object base of this realm's books.</summary>
    public required string BookBase { get; init; }
}

/// <summary>When and how well a class can use a spell.</summary>
public sealed class ClassSpellInfo
{
    public int Level { get; init; } = 1;
    public int Mana { get; init; } = 1;
    /// <summary>Base failure % before level and stat reductions.</summary>
    public int Fail { get; init; } = 25;
    /// <summary>Experience for the first cast is <c>Exp × Level</c>.</summary>
    public int Exp { get; init; }
}

/// <summary>
/// A spell (Angband class.txt "spell:" entries). <see cref="Effect"/> uses the item-effect syntax
/// plus targeted kinds (<c>bolt</c>, <c>ball</c>, <c>stone_to_mud</c>...); numbers may be expressions
/// in <c>L</c>, the caster's level, e.g. <c>bolt:none:3+(L-1)/5:4</c>. See <see cref="Magic.SpellEffects"/>.
/// </summary>
public sealed class SpellDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Realm { get; init; }
    /// <summary>Object kind id of the book it is in.</summary>
    public required string Book { get; init; }
    public required string Effect { get; init; }
    /// <summary>Needs a direction (stone to mud) rather than aiming at the nearest monster.</summary>
    public bool NeedsDirection { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyDictionary<string, ClassSpellInfo> Classes { get; init; } = new Dictionary<string, ClassSpellInfo>();
}

/// <summary>A character class (Angband class.txt).</summary>
public sealed class ClassDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Its ten titles, one for each five levels (Angband class.txt title).</summary>
    public IReadOnlyList<string> Titles { get; init; } = [];
    /// <summary>Added to the race's hit die.</summary>
    public int HitDie { get; init; }
    /// <summary>Skills at level 1: melee, bow, throw, save, stealth, disarm, device.</summary>
    public IReadOnlyDictionary<string, int> Skills { get; init; } = new Dictionary<string, int>();
    /// <summary>Skill gained per 10 levels.</summary>
    public IReadOnlyDictionary<string, int> SkillsPer10Levels { get; init; } = new Dictionary<string, int>();
    /// <summary>Adjustments to the base stats.</summary>
    public IReadOnlyDictionary<string, int> Stats { get; init; } = new Dictionary<string, int>();
    /// <summary>Melee blows ×100.</summary>
    public int Blows { get; init; } = 100;
    /// <summary>Extra experience needed, in percent, added to the race's factor.</summary>
    public int ExpFactor { get; init; }
    /// <summary>Realm id, or null for non-casters.</summary>
    public string? Realm { get; init; }
    /// <summary>Level at which the class can first cast.</summary>
    public int FirstSpellLevel { get; init; } = 1;
    public IReadOnlyList<StartItemDef> StartingKit { get; init; } = [];
    /// <summary>
    /// Class abilities (Angband player-flags): <c>STEAL</c>, <c>UNLIGHT</c>, <c>SHIELD_BASH</c>,
    /// <c>BLESS_WEAPON</c>, <c>COMBAT_REGEN</c>, and <c>IMPAIR_HP</c> (slow healing).
    /// </summary>
    public IReadOnlyList<string> Flags { get; init; } = [];
    public string Description { get; init; } = "";
}

/// <summary>Class ability flags (Angband class.txt player-flags).</summary>
public static class ClassFlags
{
    public const string Steal = "STEAL";
    public const string Unlight = "UNLIGHT";
    public const string ShieldBash = "SHIELD_BASH";
    public const string BlessWeapon = "BLESS_WEAPON";
    public const string CombatRegen = "COMBAT_REGEN";
    public const string ImpairHp = "IMPAIR_HP";
    public const string Bravery30 = "BRAVERY_30";
    public const string Beam = "BEAM";
    public const string ZeroFail = "ZERO_FAIL";
    public const string FastShot = "FAST_SHOT";
    public const string Charm = "CHARM";
    /// <summary>Picks the spells it learns; without it a book's spell is granted at random.</summary>
    public const string ChooseSpells = "CHOOSE_SPELLS";
    /// <summary>Resists nether, but holy attacks hurt more (Angband 4.2 player-calcs.c).</summary>
    public const string Evil = "EVIL";
}

/// <summary>A player race (Angband p_race.txt).</summary>
public sealed class RaceDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, int> Stats { get; init; } = new Dictionary<string, int>();
    /// <summary>Added to the class's skills.</summary>
    public IReadOnlyDictionary<string, int> Skills { get; init; } = new Dictionary<string, int>();
    public int HitDie { get; init; } = 10;
    /// <summary>Experience needed, in percent (100 = normal).</summary>
    public int ExpFactor { get; init; } = 100;
    /// <summary>Sees warm-blooded monsters in the dark up to this many squares.</summary>
    public int Infravision { get; init; }
    /// <summary>Innate protections: element ids or <c>free_act</c>, <c>blind</c>, <c>conf</c>, <c>fear</c>.</summary>
    public IReadOnlyList<string> Resists { get; init; } = [];
    /// <summary>Innate flags such as <c>REGENERATE</c>.</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];
    public string Description { get; init; } = "";
    /// <summary>AVABand's racial abilities (ava_races.json; the birth option birth_ava_races turns them off).</summary>
    public List<string> AvaAbilities { get; } = [];
    /// <summary>AVABand's racial abilities in words, for the creation screen and the character sheet.</summary>
    public string AvaAbilityText { get; set; } = "";
    /// <summary>AVABand: how much more (or less) this race carries before slowing, in percent.</summary>
    public int AvaCarryPercent { get; set; }
    /// <summary>The history chart a background starts from (Angband p_race.txt history).</summary>
    public int History { get; init; }
    /// <summary>Age at birth: the base plus 1d(mod) years.</summary>
    public BaseAndSpread Age { get; init; } = new();
    /// <summary>Height in inches, normally distributed about the base.</summary>
    public BaseAndSpread Height { get; init; } = new();
    /// <summary>Weight in pounds, normally distributed about the base.</summary>
    public BaseAndSpread Weight { get; init; } = new();
}

/// <summary>A base and its spread (p_race.txt's age, height and weight).</summary>
public sealed class BaseAndSpread
{
    public int Base { get; init; }
    public int Mod { get; init; }
}

/// <summary>
/// One of the charts a character's background is made from (Angband history.txt): the first entry
/// whose roll reaches 1d100 gives its phrase and names the chart to read next (0 ends it).
/// </summary>
public sealed class HistoryChartDef
{
    public int Chart { get; init; }
    public IReadOnlyList<HistoryEntryDef> Entries { get; init; } = [];
}

public sealed class HistoryEntryDef
{
    public int Next { get; init; }
    public int Roll { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>
/// A race's or class's ability as the birth screen and character sheet name it (Angband
/// player_property.txt): a player flag, an object flag, or (type <c>element</c>) a resistance level
/// that stands for one such ability per element.
/// </summary>
public sealed class PlayerPropertyDef
{
    /// <summary><c>player</c>, <c>object</c> or <c>element</c>.</summary>
    public required string Type { get; init; }
    public string? Code { get; init; }
    public required string Name { get; init; }
    public string Desc { get; init; } = "";
    /// <summary>For an element: the resistance level it describes (1, 3 or -1).</summary>
    public int Value { get; init; }
}
