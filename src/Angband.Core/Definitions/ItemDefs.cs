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
    /// <summary>AVABand's own: bracers (kept last, so saves made before it still line up).</summary>
    Arms,
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
    /// <summary>Kind flags every object of the base has (Angband object_base.txt flags: EASY_KNOW).</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];
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
    /// <summary>Feather falling (Angband FEATHER): float down trapdoors and pits, lightfooted over lava.</summary>
    public const string Feather = "FEATHER";
    /// <summary>Permanent fear (Angband AFRAID, the Ring of Escaping): no melee, and fear's penalties.</summary>
    public const string Afraid = "AFRAID";
    /// <summary>Slow healing (Angband IMPAIR_HP, the Ring of Open Wounds).</summary>
    public const string ImpairHp = "IMPAIR_HP";
    /// <summary>Wakes every monster that takes its turn (Angband AGGRAVATE, the Morgul blades).</summary>
    public const string Aggravate = "AGGRAVATE";
    /// <summary>Drains experience now and then (Angband DRAIN_EXP).</summary>
    public const string DrainExp = "DRAIN_EXP";
    /// <summary>A blow of more than 50 shakes the earth (Angband IMPACT).</summary>
    public const string Impact = "IMPACT";
    /// <summary>Immunity to traps (Angband TRAP_IMMUNE).</summary>
    public const string TrapImmune = "TRAP_IMMUNE";
    /// <summary>Slow mana recovery (Angband IMPAIR_MANA).</summary>
    public const string ImpairMana = "IMPAIR_MANA";
    /// <summary>No teleporting (Angband NO_TELEPORT, the curse of anti-teleportation).</summary>
    public const string NoTeleport = "NO_TELEPORT";

    public static readonly IReadOnlySet<string> Abilities = new HashSet<string>(StringComparer.Ordinal)
        { Regen, SlowDigest, HoldLife, Telepathy, Feather, Afraid, ImpairHp, Aggravate, DrainExp, Impact, TrapImmune, ImpairMana, NoTeleport };

    public static string Name(string flag) => flag switch
    {
        Regen => "regeneration",
        SlowDigest => "slow digestion",
        HoldLife => "hold life",
        Telepathy => "telepathy",
        Feather => "feather falling",
        Afraid => "fear",
        ImpairHp => "impaired healing",
        Aggravate => "aggravation",
        DrainExp => "experience drain",
        Impact => "earthquakes",
        TrapImmune => "trap immunity",
        ImpairMana => "impaired mana recovery",
        NoTeleport => "no teleportation",
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
    /// <summary>Extra might (Angband MIGHT): added to a launcher's multiplier.</summary>
    public const string Might = "might";
    /// <summary>Damage reduction (Angband DAM_RED): taken off every hurt.</summary>
    public const string DamRed = "dam_red";
    /// <summary>Extra moves (Angband MOVES): each step takes less of a turn.</summary>
    public const string Moves = "moves";
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
    /// <summary>Its own colour (Angband object.txt graphics), for kinds without a flavour; null takes the base's.</summary>
    public string? Color { get; init; }
    /// <summary>Elements this kind is proof against, though its base hates them (Angband IGNORE_*).</summary>
    public IReadOnlyList<string> Ignore { get; init; } = [];
    public int Level { get; init; }
    /// <summary>Relative commonness; 0 means never randomly generated.</summary>
    public int Commonness { get; init; } = 10;
    /// <summary>
    /// The shallowest depth it is made at (Angband object.txt alloc); without one, its level. As in
    /// 4.2.5, a kind with an alloc range is made across that range whatever its level.
    /// </summary>
    public int? MinDepth { get; init; }
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
    /// <summary>The power of each of its curses (Angband curse:name:power; 100 is permanent).</summary>
    public IReadOnlyDictionary<string, int> CursePowers { get; init; } = new Dictionary<string, int>();
    /// <summary>AVABand's bags of holding: carried in the pack, this much more (percent) can be carried before slowing.</summary>
    public int CarryPercent { get; init; }
    /// <summary>AVABand's bags of holding: carried in the pack, this many more things fit in it.</summary>
    public int PackSlots { get; init; }
    /// <summary>AVABand's socketed bracers: how many gems they take.</summary>
    public int Sockets { get; init; }
    /// <summary>What using it does, e.g. <c>heal:20; cure:blind</c>. See <see cref="Items.ItemEffects"/>.</summary>
    public string? Effect { get; init; }
    /// <summary>Starting fuel for light sources, in player turns (0 = needs none).</summary>
    public int Fuel { get; init; }
    /// <summary>Stack size when generated, e.g. <c>5d4</c> for arrows.</summary>
    public Dice StackSize { get; init; } = Dice.Constant(1);
    /// <summary>
    /// The percentage chance a generated one comes as a pile of <see cref="StackSize"/> (Angband
    /// object.txt pile, e.g. <c>pile:25:2</c>: a quarter of the time, two); otherwise it is one.
    /// </summary>
    public int PileChance { get; init; } = 100;
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
    /// <summary>Particular kinds it may also be made on (Angband ego_item.txt <c>item:</c>): lanterns, torches.</summary>
    public IReadOnlyList<string> Kinds { get; init; } = [];
    public int Level { get; init; }
    public int Commonness { get; init; } = 10;
    public int MaxDepth { get; init; } = 127;
    /// <summary>
    /// A random extra power when made (Angband RAND_*): <c>sustain</c>, <c>power</c> (a protection or
    /// ability), <c>high_resist</c>, <c>base_resist</c>, or <c>resist_or_power</c> (one in three a power).
    /// </summary>
    public string? RandomPower { get; init; }
    /// <summary>
    /// The least the finished item may have (Angband min-combat and min-values): <c>to_h</c>,
    /// <c>to_d</c>, <c>to_a</c> and modifiers by id.
    /// </summary>
    public IReadOnlyDictionary<string, int> Minimums { get; init; } = new Dictionary<string, int>();
    /// <summary>Flags it takes away (Angband flags-off): an Everburning lantern no longer takes fuel.</summary>
    public IReadOnlyList<string> FlagsOff { get; init; } = [];
    /// <summary>Extra rolled bonuses: <c>1d5</c> etc., added on top of normal magic.</summary>
    public Dice ToHit { get; init; } = Dice.Zero;
    public Dice ToDam { get; init; } = Dice.Zero;
    public Dice ToAc { get; init; } = Dice.Zero;
    public IReadOnlyDictionary<string, int> Modifiers { get; init; } = new Dictionary<string, int>();
    /// <summary>AVABand's (Porter) ego: worn, this much more (percent) can be carried before slowing.</summary>
    public int CarryPercent { get; init; }
    /// <summary>Random modifiers (Angband random-value syntax), rolled when the ego is applied.</summary>
    public IReadOnlyDictionary<string, string> Rolls { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<SlayDef> Slays { get; init; } = [];
    public IReadOnlyList<BrandDef> Brands { get; init; } = [];
    public IReadOnlyList<string> Resists { get; init; } = [];
    /// <summary>Ability flags (see <see cref="ItemFlags"/>).</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];
    /// <summary>Curses the ego always carries (e.g. a "Morgul" weapon).</summary>
    public IReadOnlyList<string> Curses { get; init; } = [];
    public IReadOnlyDictionary<string, int> CursePowers { get; init; } = new Dictionary<string, int>();

    /// <summary>Whether it can be made on <paramref name="kind"/>: its bases, or its particular kinds.</summary>
    public bool Fits(ObjectKindDef kind) => Bases.Contains(kind.Base) || Kinds.Contains(kind.Id);

    /// <summary>The bases it can appear on, including those of its particular kinds.</summary>
    public IEnumerable<string> AllBases(GameData data) =>
        Bases.Concat(Kinds.Select(k => data.Object(k)?.Base).OfType<string>()).Distinct();
}

/// <summary>A unique named object (Angband artifact.txt). Each appears at most once per game.</summary>
public sealed class ArtifactDef
{
    public required string Id { get; init; }
    /// <summary>Full name suffix, e.g. <c>'Narthanc'</c> or <c>of Galadriel</c>.</summary>
    public required string Name { get; init; }
    public required string Kind { get; init; }
    /// <summary>The shallowest depth it is made at (Angband artifact.txt alloc minimum; deeper than that only by luck).</summary>
    public int Level { get; init; }
    /// <summary>The percentage chance, once its kind is being made within its depths (Angband alloc chance).</summary>
    public int AllocChance { get; init; } = 10;
    /// <summary>The deepest it is made at (Angband alloc maximum).</summary>
    public int MaxDepth { get; init; } = 127;
    /// <summary>Its own weight, in tenths of a pound (Angband artifact.txt weight); none for its kind's.</summary>
    public int? Weight { get; init; }
    /// <summary>Elements it makes you immune to (Angband RES_x[3]).</summary>
    public IReadOnlyList<string> Immunities { get; init; } = [];
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
    public IReadOnlyDictionary<string, int> CursePowers { get; init; } = new Dictionary<string, int>();
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
    /// <summary>Stats, speed, stealth... it changes (Angband curse values).</summary>
    public IReadOnlyDictionary<string, int> Modifiers { get; init; } = new Dictionary<string, int>();
    /// <summary>Elements it makes you vulnerable to (Angband RES_x[-1]).</summary>
    public IReadOnlyList<string> Vulnerabilities { get; init; } = [];
    /// <summary>Elements it makes you resist, in exchange (burning up resists cold).</summary>
    public IReadOnlyList<string> Resists { get; init; } = [];
    /// <summary>Flags it gives: AGGRAVATE, IMPAIR_HP, IMPAIR_MANA, AFRAID, NO_TELEPORT.</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];
    /// <summary>What it does now and then: a trap effect string (e.g. <c>teleport:40</c>, <c>timed:poisoned:10+d10</c>).</summary>
    public string? Effect { get; init; }
    /// <summary>How long between its effects, in player turns (a random value, rolled afresh each time).</summary>
    public string Time { get; init; } = "0";
    public string EffectMessage { get; init; } = "";
    /// <summary>Which object bases can carry it (empty = any wearable).</summary>
    public IReadOnlyList<string> Bases { get; init; } = [];
    /// <summary>Curses it can't share an object with (teleportation and anti-teleportation).</summary>
    public IReadOnlyList<string> Conflicts { get; init; } = [];
    /// <summary>Object properties that keep it off (cowardice won't go on what protects from fear).</summary>
    public IReadOnlyList<string> ConflictFlags { get; init; } = [];
}

/// <summary>Unknown-object appearances for one flavour group (potion colours, ring stones...).</summary>
public sealed class FlavorGroupDef
{
    public required string Id { get; init; }
    /// <summary>How an unknown object is named: <c>{flavor} Potion</c>.</summary>
    public string Pattern { get; init; } = "{flavor} {base}";
    public IReadOnlyList<FlavorDef> Flavors { get; init; } = [];
    /// <summary>Flavours kept for particular kinds (Angband's fixed lines: the One Ring is always Plain Gold).</summary>
    public IReadOnlyList<FixedFlavorDef> Fixed { get; init; } = [];
    /// <summary>
    /// Scrolls: titles are made instead, of words built letter by letter from these (Angband
    /// randname_make over names.txt's scroll section).
    /// </summary>
    public IReadOnlyList<string> TitleWords { get; init; } = [];
}

/// <summary>A flavour that always goes to one kind.</summary>
public sealed class FixedFlavorDef
{
    public required string Kind { get; init; }
    public required string Name { get; init; }
    public string Color { get; init; } = "White";
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

/// <summary>
/// What a property of an object is worth (Angband object_property.txt): its power (for each point
/// of a modifier), its weight in the stat-bonus total (<see cref="Mult"/>), its set (sustain,
/// protection, misc ability...) and how that power multiplies on each kind of object (type-mult;
/// 1 where none is given).
/// </summary>
public sealed class ObjectPropertyDef
{
    /// <summary><c>stat</c>, <c>mod</c>, <c>flag</c>, <c>ignore</c>, <c>resistance</c>, <c>vulnerability</c> or <c>immunity</c>.</summary>
    public required string Type { get; init; }
    public string Code { get; init; } = "";
    /// <summary>AVABand's name for it: a modifier, a flag, a resist-list id or an element.</summary>
    public required string Id { get; init; }
    public int Power { get; init; }
    public int Mult { get; init; }
    public string? Subtype { get; init; }
    public IReadOnlyDictionary<string, int> TypeMult { get; init; } = new Dictionary<string, int>();

    public int TypeMultFor(string baseId) => TypeMult.TryGetValue(baseId, out var m) ? m : 1;
}

/// <summary>A slay's worth (Angband slay.txt): its power and its O-combat multiplier, in tenths.</summary>
public sealed class SlayTypeDef
{
    public string Code { get; init; } = "";
    public required string Flag { get; init; }
    public int Multiplier { get; init; }
    public int OMultiplier { get; init; }
    public int Power { get; init; }
}

/// <summary>A brand's worth (Angband brand.txt): its power and its O-combat multiplier, in tenths.</summary>
public sealed class BrandTypeDef
{
    public string Code { get; init; } = "";
    public required string Element { get; init; }
    public int Multiplier { get; init; }
    public int OMultiplier { get; init; }
    public int Power { get; init; }
}
