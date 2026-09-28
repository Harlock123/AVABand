using Angband.Core.Randomness;

namespace Angband.Core.Definitions;

/// <summary>
/// A damage element (Angband projection.txt). Resistance divides damage by
/// <see cref="Divisor"/> and multiplies by <see cref="Numerator"/>, once per resist level.
/// </summary>
public sealed class ElementDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Numerator { get; init; } = 1;
    /// <summary>Rolled per hit, e.g. <c>3</c> for fire or <c>1d6+6</c> for chaos.</summary>
    public Dice Divisor { get; init; } = Dice.Constant(3);
    /// <summary>Monster flag granting immunity (e.g. <c>IM_FIRE</c>); brands do nothing against it.</summary>
    public string? ImmunityFlag { get; init; }
    /// <summary>Monster flag making it take extra damage (e.g. <c>HURT_FIRE</c>).</summary>
    public string? VulnerabilityFlag { get; init; }
}

/// <summary>What happens when a timed effect gets another dose while active.</summary>
public enum TimedStacking
{
    /// <summary>Add to the current duration.</summary>
    Increase,
    /// <summary>Ignore new doses while active (e.g. paralysis, to prevent perma-lock).</summary>
    NoStack,
    /// <summary>Keep the larger of the two.</summary>
    Max,
}

/// <summary>A status effect with a duration (Angband player_timed.txt).</summary>
public sealed class TimedEffectDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string OnBegin { get; init; } = "";
    public string OnEnd { get; init; } = "";
    public string OnIncrease { get; init; } = "";
    public int Max { get; init; } = 10_000;
    public TimedStacking Stacking { get; init; } = TimedStacking.Increase;
    /// <summary>Whether it counts as harmful (cleared by curing, blocks regeneration, etc.).</summary>
    public bool Harmful { get; init; } = true;
}

/// <summary>How a monster attacks (bite, claw, touch...).</summary>
public sealed class BlowMethodDef
{
    public required string Id { get; init; }
    /// <summary>Message after the monster name, e.g. <c>bites you</c>.</summary>
    public required string Message { get; init; }
    public string MissMessage { get; init; } = "misses you";
    /// <summary>Hard hits of this kind can cut.</summary>
    public bool Cut { get; init; }
    /// <summary>Hard hits of this kind can stun.</summary>
    public bool Stun { get; init; }
    /// <summary>The blow only works if the monster can touch the player (future: invisibility, etc.).</summary>
    public bool Miss { get; init; } = true;
}

/// <summary>What a monster blow does on a hit (Angband blow_effects.txt).</summary>
public sealed class BlowEffectDef
{
    public required string Id { get; init; }
    /// <summary>Accuracy bonus: hit chance is <c>power + 3 * monster level</c> vs. armour.</summary>
    public int Power { get; init; }
    /// <summary>Damage is of this element (and resisted accordingly); null means plain physical.</summary>
    public string? Element { get; init; }
    /// <summary>Plain hits are reduced by armour.</summary>
    public bool ArmourReduces { get; init; }
    /// <summary>Status effect inflicted on a hit.</summary>
    public string? Timed { get; init; }
    /// <summary>Duration is <c>DurationBase + randint1(damage or level)</c>.</summary>
    public int DurationBase { get; init; }
    /// <summary><c>damage</c> or <c>level</c>: what the random part of the duration scales with.</summary>
    /// <summary>What the random part of the duration scales with: <c>level</c>, <c>half_level</c> or <c>damage</c>.</summary>
    public string DurationScale { get; init; } = "level";
    /// <summary>Whether the player's saving throw can shrug the status off.</summary>
    public bool Save { get; init; }
    /// <summary>Resist/protection that prevents the status entirely (e.g. <c>free_act</c>, <c>pois</c>).</summary>
    public string? PreventedBy { get; init; }
}

public sealed class MonsterBlowDef
{
    public required string Method { get; init; }
    public string Effect { get; init; } = "hurt";
    public Dice Damage { get; init; } = Dice.Zero;
}

/// <summary>
/// A monster's escort (Angband monster.txt "friends" / "friends-base"): with <see cref="Chance"/>
/// percent odds, <see cref="Number"/> more monsters — of the same race, a named race, or any race
/// of a base (matched by its symbol, <see cref="Glyph"/>).
/// </summary>
public sealed class MonsterFriendDef
{
    public int Chance { get; init; } = 100;
    public Dice Number { get; init; } = Dice.Constant(1);
    /// <summary>A race id, or <c>same</c> for more of the monster's own race; null for a base.</summary>
    public string? Race { get; init; }
    /// <summary>Angband monster base name (e.g. <c>person</c>), for a base escort.</summary>
    public string? Base { get; init; }
    /// <summary>The base's symbol: escorts of a base are drawn from races shown with it.</summary>
    public char? Glyph { get; init; }

    public bool IsSame => string.Equals(Race, "same", StringComparison.Ordinal);

    /// <summary>Angband's group role for these escorts: <c>bodyguard</c> ones stay by their leader and never lose heart.</summary>
    public string? Role { get; init; }

    public bool IsBodyguard => string.Equals(Role, "bodyguard", StringComparison.Ordinal);
}

/// <summary>A kind of monster (Angband monster.txt).</summary>
public sealed class MonsterRaceDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Plural { get; init; }
    public char Glyph { get; init; } = '?';
    public string Color { get; init; } = "White";
    /// <summary>Native dungeon level.</summary>
    public int Depth { get; init; }
    /// <summary>Angband spell-power, where it differs from the level (a dozen monsters).</summary>
    public int? SpellPower { get; init; }
    /// <summary>The power of its spells: the spell power, or else its level (Angband race->spell_power).</summary>
    public int Power => SpellPower ?? Depth;
    /// <summary>1-in-N commonness; higher is rarer.</summary>
    public int Rarity { get; init; } = 1;
    /// <summary>Speed relative to normal (+10 = fast).</summary>
    public int Speed { get; init; }
    /// <summary>Average hit points.</summary>
    public int HitPoints { get; init; } = 1;
    public int Armour { get; init; }
    /// <summary>Initial sleep counter (0 = always awake).</summary>
    public int Sleep { get; init; }
    /// <summary>How far it hears the player, in grids of sound travel (reduced by player stealth).</summary>
    public int Hearing { get; init; } = 20;
    /// <summary>How old (in player moves) a scent trail it can still follow is; 0 = no sense of smell.</summary>
    public int Smell { get; init; }
    /// <summary>
    /// Casts a spell on 1 turn in N (0 = never): Angband's spell-freq, for everything but its
    /// innate attacks. A 1-in-N frequency is a 100/N percent chance, as Angband stores it.
    /// </summary>
    public int SpellFrequency { get; init; }
    /// <summary>Uses an innate attack — breath, arrows, boulders, spit, shrieks — on 1 turn in N (Angband innate-freq).</summary>
    public int InnateFrequency { get; init; }
    /// <summary>Spell ids from monster_spells.json.</summary>
    public IReadOnlyList<string> Spells { get; init; } = [];
    /// <summary>Experience value (multiplied by level, divided by player level).</summary>
    public int Experience { get; init; }
    public IReadOnlyList<MonsterBlowDef> Blows { get; init; } = [];
    public IReadOnlySet<string> Flags { get; init; } = new HashSet<string>();
    /// <summary>Who comes with it when it is placed (Angband friends / friends-base lines).</summary>
    public IReadOnlyList<MonsterFriendDef> Friends { get; init; } = [];
    /// <summary>Races it can change into with the SHAPECHANGE spell (Angband "shape:" lines).</summary>
    public IReadOnlyList<string> Shapes { get; init; } = [];
    /// <summary>Object kinds a mimic poses as until found out (Angband "mimic:" lines).</summary>
    public IReadOnlyList<string> Mimics { get; init; } = [];
    public string Description { get; init; } = "";

    public bool Has(string flag) => Flags.Contains(flag);
    public bool IsUnique => Has(MonsterFlags.Unique);
}

/// <summary>Monster flag names the engine understands. Data may add others for future systems.</summary>
public static class MonsterFlags
{
    public const string Unique = "UNIQUE";
    public const string Male = "MALE";
    public const string Female = "FEMALE";
    public const string NeverMove = "NEVER_MOVE";
    public const string NeverBlow = "NEVER_BLOW";
    public const string Rand25 = "RAND_25";
    public const string Rand50 = "RAND_50";
    public const string OpenDoor = "OPEN_DOOR";
    public const string BashDoor = "BASH_DOOR";
    public const string NoFear = "NO_FEAR";
    public const string NoConf = "NO_CONF";
    public const string NoSleep = "NO_SLEEP";
    public const string NoStun = "NO_STUN";
    public const string Regenerate = "REGENERATE";
    public const string Animal = "ANIMAL";
    public const string Evil = "EVIL";
    public const string Orc = "ORC";
    public const string Undead = "UNDEAD";
    /// <summary>Breeds explosively (Angband MULTIPLY).</summary>
    public const string Multiply = "MULTIPLY";
    /// <summary>Pack tactics: lurks out of sight until the player is in the open.</summary>
    public const string GroupAi = "GROUP_AI";
    /// <summary>Pushes past weaker monsters.</summary>
    public const string MoveBody = "MOVE_BODY";
    /// <summary>Kills weaker monsters in its way.</summary>
    public const string KillBody = "KILL_BODY";
    /// <summary>Never fails to cast (Angband SMART-ish casters skip failure checks).</summary>
    public const string Smart = "SMART";
    public const string Stupid = "STUPID";
    /// <summary>Unseen without see invisible.</summary>
    public const string Invisible = "INVISIBLE";
    /// <summary>Moves through walls (not permanent rock).</summary>
    public const string PassWall = "PASS_WALL";
    /// <summary>Tunnels through walls, turning them to floor.</summary>
    public const string KillWall = "KILL_WALL";
    /// <summary>Stone to mud hurts it.</summary>
    public const string HurtRock = "HURT_ROCK";
    /// <summary>A quest monster (Sauron, Morgoth): placed only by its quest.</summary>
    public const string Questor = "QUESTOR";
    /// <summary>Never generated out of depth.</summary>
    public const string ForceDepth = "FORCE_DEPTH";
    /// <summary>Starts camouflaged (Angband UNAWARE): a mimic or lurker the player hasn't noticed.</summary>
    public const string Unaware = "UNAWARE";
    public const string Troll = "TROLL";
    public const string Giant = "GIANT";
    public const string Dragon = "DRAGON";
    public const string Demon = "DEMON";
}

/// <summary>
/// Angband 4.2 projection types a player can't resist (projection.txt), used only for what they do
/// besides damage: they aren't elements in <c>elements.json</c>, so no gear resists them.
/// </summary>
public static class Projections
{
    public static readonly IReadOnlySet<string> Unresistable =
        new HashSet<string>(["water", "ice", "gravity", "inertia", "force", "time", "plasma", "mana"], StringComparer.Ordinal);
}

/// <summary>What a monster spell does.</summary>
public enum MonsterSpellKind
{
    /// <summary>A projectile at the player; blocked by other monsters.</summary>
    Bolt,
    /// <summary>An area attack centred on the player.</summary>
    Ball,
    /// <summary>Breath: damage is the caster's current hit points divided by <c>BreathDivisor</c>.</summary>
    Breath,
    /// <summary>A timed effect on the player (blindness, confusion...), usually allowing a save.</summary>
    Status,
    /// <summary>Direct wounds, negated by a saving throw.</summary>
    Wound,
    /// <summary>Short-range teleport of the caster.</summary>
    Blink,
    /// <summary>Long-range teleport of the caster.</summary>
    Teleport,
    /// <summary>Brings the player next to the caster.</summary>
    TeleportTo,
    Heal,
    Summon,
    /// <summary>Wakes and alerts every monster nearby.</summary>
    Shriek,
    /// <summary>Drains the player's spell points to heal the caster.</summary>
    DrainMana,
    /// <summary>Spins webs around the caster (Angband WEAVE).</summary>
    Web,
    /// <summary>A storm: balls of water, lightning and ice at once (Angband STORM).</summary>
    Storm,
    /// <summary>Psychic attack: damage plus a status (confusion...), negated by a saving throw.</summary>
    Mind,
    /// <summary>The caster speeds itself up.</summary>
    Haste,
    /// <summary>The player forgets the map.</summary>
    Forget,
    /// <summary>Creates traps around the player.</summary>
    Traps,
    /// <summary>Teleports the player away.</summary>
    TeleportAway,
    /// <summary>Sends the player up or down a level.</summary>
    TeleportLevel,
    /// <summary>The caster teleports next to the player.</summary>
    TeleportSelfTo,
    /// <summary>Heals a wounded monster of the same kind nearby.</summary>
    HealKin,
    /// <summary>Darkens the area around the player (and may blind).</summary>
    Darkness,
    /// <summary>The caster changes into one of its shapes.</summary>
    Shapechange,
}

/// <summary>A monster spell or innate ranged attack (Angband monster_spell.txt).</summary>
public sealed class MonsterSpellDef
{
    public required string Id { get; init; }
    public MonsterSpellKind Kind { get; init; }
    /// <summary>Innate attacks (arrows, breath, spit) never fail and work while confused.</summary>
    public bool Innate { get; init; }
    public string? Element { get; init; }
    public Dice Damage { get; init; } = Dice.Zero;
    /// <summary>Adds <c>monster level / LevelDivisor</c> to the damage (0 = no scaling).</summary>
    public int LevelDivisor { get; init; }
    /// <summary>Adds this percentage of the monster's level to the damage (Angband's spell-power scaling).</summary>
    public int LevelPercent { get; init; }
    public int BreathDivisor { get; init; } = 3;
    /// <summary>Maximum breath damage.</summary>
    public int BreathCap { get; init; } = 1600;
    public string? Timed { get; init; }
    public Dice Duration { get; init; } = Dice.Zero;
    /// <summary>Player saving throw negates it.</summary>
    public bool Save { get; init; }
    /// <summary>
    /// Angband 4.2 WOUND: damage and cuts grow with the caster's spell power instead of fixed dice
    /// (<see cref="Damage"/> is unused).
    /// </summary>
    public bool PowerScaled { get; init; }
    /// <summary>For summons: no uniques (Angband summon.txt <c>uniques:0</c>).</summary>
    public bool SummonNoUniques { get; init; }
    /// <summary>For summons: uniques only (the Ringwraiths, "summon uniques").</summary>
    public bool SummonUniquesOnly { get; init; }
    /// <summary>For summons: glyphs to fall back on when nothing suitable is found (Angband <c>fallback</c>).</summary>
    public string? SummonFallbackGlyphs { get; init; }
    /// <summary>Resist/protection id that negates the status (e.g. <c>free_act</c>, <c>conf</c>).</summary>
    public string? PreventedBy { get; init; }
    /// <summary>For summons: same glyph as the caster ("kin").</summary>
    public bool Kin { get; init; }
    /// <summary>For summons: only monsters shown with one of these glyphs (e.g. <c>Z</c> for hounds).</summary>
    public string? SummonGlyphs { get; init; }
    /// <summary>For summons: only monsters with this flag (e.g. <c>UNDEAD</c>).</summary>
    public string? SummonFlag { get; init; }
    public Dice Count { get; init; } = Dice.Constant(1);
    /// <summary>Hit points healed per monster level.</summary>
    public int HealPerLevel { get; init; } = 6;
    /// <summary>Useful when fleeing (blink, teleport, heal).</summary>
    public bool Escape { get; init; }
    /// <summary>Shown when the player can see the caster; <c>{name}</c> is replaced.</summary>
    public string Message { get; init; } = "{name} casts a spell.";
    /// <summary>The sound it makes (Angband monster_spell.txt msgt: BR_FROST, SUM_UNDEAD…); bolts and balls have none.</summary>
    public string? Sound { get; init; }
    /// <summary>Shown when the caster is unseen.</summary>
    public string UnseenMessage { get; init; } = "Something mumbles.";
    /// <summary>
    /// How monster memory describes the spell (Angband monster_spell.txt lore lines), by spell power:
    /// the last level whose <see cref="MonsterSpellLore.Power"/> the caster reaches applies.
    /// </summary>
    public IReadOnlyList<MonsterSpellLore> Lore { get; init; } = [];

    /// <summary>The lore level for a caster of this spell power (Angband mon_spell_lore_description).</summary>
    public MonsterSpellLore? LoreFor(int spellPower) => Lore.LastOrDefault(l => spellPower >= l.Power) ?? Lore.FirstOrDefault();
}

/// <summary>
/// One level of a monster spell's lore (Angband monster_spell.txt): the phrase used in recall and the
/// colours it is shown in — normally, when the player resists it, and when the player is immune.
/// </summary>
public sealed class MonsterSpellLore
{
    /// <summary>Spell power from which this level applies (Angband power-cutoff).</summary>
    public int Power { get; init; }
    public string Text { get; init; } = "";
    /// <summary>What is said when a caster of this power uses it (seen / unseen / saved against), if not the spell's own.</summary>
    public string? Message { get; init; }
    public string? UnseenMessage { get; init; }
    public string? SaveMessage { get; init; }
    public string Color { get; init; } = "White";
    public string? ResistColor { get; init; }
    public string? ImmuneColor { get; init; }
    /// <summary>The player gets a saving throw against it (Angband: the level has a save message).</summary>
    public bool Save { get; init; }
}
