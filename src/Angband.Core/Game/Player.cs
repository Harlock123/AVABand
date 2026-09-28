using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Time;

namespace Angband.Core.Game;

/// <summary>
/// The player character. "Base" values are the character's own (placeholders for a level 1
/// warrior until character creation); the others are recalculated from equipment by
/// <see cref="GameSession.RecalculateBonuses"/> (Angband calc_bonuses).
/// </summary>
public sealed class Player : IActor
{
    public const int PlayerActorId = 0;

    public int ActorId => PlayerActorId;
    public int Energy { get; set; }
    public bool IsActive => true;

    public string Name { get; set; } = "Adventurer";
    public Loc Position { get; set; }
    public int Depth { get; set; }
    public int MaxDepth { get; set; }

    public Definitions.ClassDef? Class { get; set; }
    public Definitions.RaceDef? Race { get; set; }
    /// <summary>Base stats chosen at birth (before race and class adjustments).</summary>
    public Dictionary<string, int> BaseStats { get; } = [];
    /// <summary>Fractional hit points from Constitution carried between level-ups (x100).</summary>
    public int ConHpRemainder { get; set; }
    /// <summary>Innate regeneration (half-trolls).</summary>
    public bool Regenerates { get; set; }
    public int Infravision { get; set; }
    /// <summary>Infravision including gear and timed effects (kept by RecalculateBonuses).</summary>
    public int TotalInfravision { get; set; }

    /// <summary>World ticks until Word of Recall activates (0 = not active).</summary>
    public int RecallTimer { get; set; }
    /// <summary>
    /// Where Word of Recall takes you from town (Angband recall_depth); 0 means the deepest level
    /// reached. Set by choosing a level (persistent dungeons) or "set recall depth to current depth".
    /// </summary>
    public int RecallDepth { get; set; }
    /// <summary>World ticks until Deep Descent takes effect (0 = not active).</summary>
    public int DeepDescentTimer { get; set; }
    public int Level { get; set; } = 1;
    /// <summary>Highest level reached (experience drain can lower the current level later).</summary>
    public int MaxLevel { get; set; } = 1;
    /// <summary>Experience needed per level is scaled by this percentage (race + class).</summary>
    public int ExpFactor { get; set; } = 100;
    /// <summary>Hit points gained per level: randint1(HitDie).</summary>
    public int HitDie { get; set; } = 10;

    /// <summary>The five stats (str, int, wis, dex, con): 3..18, then 19 = 18/10 ... 40 = 18/220.</summary>
    public Dictionary<string, int> Stats { get; } = new() { ["str"] = 15, ["int"] = 15, ["wis"] = 15, ["dex"] = 15, ["con"] = 15 };

    /// <summary>
    /// Stats before equipment and drain: birth values (race and class included) plus permanent gains.
    /// <see cref="Stats"/> is recomputed from these by RecalculateBonuses.
    /// </summary>
    public Dictionary<string, int> NaturalStats { get; } = [];

    /// <summary>Points drained from each stat (restored on level gain or by restoring effects).</summary>
    public Dictionary<string, int> StatDrain { get; } = [];

    /// <summary>Highest experience reached; drained experience can be restored up to this.</summary>
    public long MaxExperience { get; set; }

    /// <summary>Hit points gained at each level above 1 (index 0 = level 2), so drained levels can be undone.</summary>
    public List<int> HpGains { get; } = [];

    /// <summary>Magic device skill (Angband skill_dev).</summary>
    public int SkillDevice { get; set; } = 20;

    public int MaxMana { get; set; }
    public int Mana { get; set; }
    /// <summary>Fractional spell points (1/65536ths).</summary>
    public int ManaFraction { get; set; }
    /// <summary>Spells learned (ids), in the order learned.</summary>
    public List<string> LearnedSpells { get; } = [];
    /// <summary>Spells cast at least once (the first cast gives experience).</summary>
    public HashSet<string> CastSpells { get; } = [];
    public long Experience { get; set; }
    public int ExperienceFraction { get; set; }
    public long Gold { get; set; }
    /// <summary>Food counter (see <see cref="HungerLevel"/>); new games start just below full.</summary>
    public int Food { get; set; } = 9999;

    /// <summary>Ability flags from worn equipment (<see cref="Definitions.ItemFlags"/>), kept by RecalculateBonuses.</summary>
    public HashSet<string> GearFlags { get; } = new(StringComparer.Ordinal);

    public bool HasGearFlag(string flag) => GearFlags.Contains(flag);

    public int MaxHp { get; set; } = 20;
    public int Hp { get; set; } = 20;
    /// <summary>Fractional hit points (1/65536ths) for Angband-style regeneration.</summary>
    public int HpFraction { get; set; }
    public bool IsDead { get; set; }
    /// <summary>The shape the player has taken (Angband shape.txt id), or null for their own.</summary>
    public string? Shape { get; set; }

    /// <summary>While scrambled: which natural stat each stat reads from.</summary>
    public Dictionary<string, string> StatScramble { get; } = [];

    /// <summary>Killed Morgoth: the game is won (Angband total_winner).</summary>
    public bool IsWinner { get; set; }
    public string? KilledBy { get; set; }
    public bool IsResting { get; set; }

    public Inventory Inventory { get; set; } = new();
    public TimedEffects Timed { get; } = new();
    public bool IsBlind => Timed.Has(TimedIds.Blind);
    /// <summary>Paralysed, or stunned to the point of being knocked out.</summary>
    public bool IsIncapacitated => Timed.Has(TimedIds.Paralyzed) || Timed[TimedIds.Stun] >= 100;

    // --- The character's own values -------------------------------------------------------
    public int BaseSpeed { get; set; }
    public int BaseArmour { get; set; }
    public int BaseToHit { get; set; }
    public int BaseToDam { get; set; }
    public int BaseStealth { get; set; } = 2;
    public int BaseBlows { get; set; } = 200;
    public int BaseShots { get; set; } = 10;
    /// <summary>Carrying more than half this (tenths of a pound) slows the player.</summary>
    public int WeightLimit { get; set; } = 1500;
    /// <summary>Racial/temporary resistances: element id to level (-1 vulnerable, 1 resist, 3 immune).</summary>
    public Dictionary<string, int> IntrinsicResists { get; } = [];

    // --- Derived from equipment ---------------------------------------------------------------
    public int EquipmentSpeed { get; set; }
    public int Speed => BaseSpeed + EquipmentSpeed + (Timed.Has(TimedIds.Fast) ? 10 : 0) - (Timed.Has(TimedIds.Slow) ? 10 : 0);
    public int Armour { get; set; }
    public int ToHit { get; set; }
    public int ToDam { get; set; }
    public int Stealth { get; set; } = 2;
    /// <summary>Melee blows per turn ×100 (250 = 2.5 blows).</summary>
    public int Blows { get; set; } = 200;
    /// <summary>Shots per turn ×10.</summary>
    public int Shots { get; set; } = 10;
    public int LightRadius { get; set; }
    /// <summary>Combined resistances (intrinsic and equipment).</summary>
    public Dictionary<string, int> Resists { get; } = [];

    public int SkillMelee { get; set; } = 70;
    public int SkillBow { get; set; } = 55;
    public int SkillThrow { get; set; } = 55;
    public int SkillSave { get; set; } = 18;
    /// <summary>Disarm/lockpicking skill used for locked doors.</summary>
    public int DisarmSkill { get; set; } = 30;
    /// <summary>Disarming magical traps (runes) — Angband 4.2's skill-disarm-magic; <see cref="DisarmSkill"/> is the physical one.</summary>
    public int DisarmMagicSkill { get; set; } = 30;

    /// <summary>To-hit including the stun penalty.</summary>
    public int EffectiveToHit => ToHit - StunPenalty;
    public int EffectiveToDam => ToDam - StunPenalty;

    /// <summary>Angband: stun costs 5 to-hit/to-dam, heavy stun (over 50) costs 20.</summary>
    private int StunPenalty => Timed[TimedIds.Stun] switch { > 50 => 20, > 0 => 5, _ => 0 };
}
