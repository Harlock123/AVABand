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
}
