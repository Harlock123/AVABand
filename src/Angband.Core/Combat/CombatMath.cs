using Angband.Core.Definitions;
using Angband.Core.Randomness;

namespace Angband.Core.Combat;

/// <summary>Grades of critical hit, for messages and sounds.</summary>
public enum CriticalGrade
{
    None,
    Good,
    Great,
    Superb,
    HighGreat,
    HighSuperb,
}

/// <summary>
/// Angband 4.2 combat formulas as pure functions. Everything random takes the <see cref="GameRandom"/>
/// explicitly, so results are reproducible and unit-testable.
/// </summary>
public static class CombatMath
{
    /// <summary>To-hit bonuses are worth this many points of skill (Angband BTH_PLUS_ADJ).</summary>
    public const int BthPlusAdj = 3;

    /// <summary>Armour above this stops reducing melee damage further.</summary>
    public const int MaxArmourReduction = 240;

    /// <summary>
    /// Angband test_hit (hit_chance and random_chance_check): out of 10,000, a hit 1,200 times
    /// always, a miss 500 times, and in between as often as the chance (at least 9) exceeds two thirds
    /// of the armour, in proportion to the chance. Unseen targets halve the chance first.
    /// </summary>
    public static bool TestHit(GameRandom rng, int chance, int armour, bool visible) =>
        rng.RandInt0(HundredPercent) >= HundredPercent - HitNumerator(chance, armour, visible);

    /// <summary>The probability (0..1) that <see cref="TestHit"/> succeeds, for UI and tests.</summary>
    public static double HitProbability(int chance, int armour, bool visible) =>
        HitNumerator(chance, armour, visible) / (double)HundredPercent;

    private const int HundredPercent = 10000, AlwaysHit = 1200, AlwaysMiss = 500;

    private static int HitNumerator(int chance, int armour, bool visible)
    {
        if (!visible) chance /= 2;
        var toHit = Math.Max(9, chance);
        long numerator = Math.Max(0, toHit - armour * 2 / 3);
        numerator = HundredPercent * numerator / toHit;
        numerator = numerator * (HundredPercent - AlwaysMiss - AlwaysHit) / HundredPercent;
        return (int)numerator + AlwaysHit;
    }

    /// <summary>Melee hit chance: skill plus to-hit bonuses (Angband chance_of_melee_hit).</summary>
    public static int MeleeChance(int skill, int toHit) => skill + toHit * BthPlusAdj;

    /// <summary>Missile hit chance, reduced by distance (Angband chance_of_missile_hit).</summary>
    public static int MissileChance(int skill, int toHit, int distance) => skill + toHit * BthPlusAdj - distance;

    /// <summary>
    /// Angband critical_melee (constants.txt melee-critical): a chance from the weapon's weight, to-hit
    /// (ten more against a <paramref name="debuffed"/> monster), level and skill; then a power from the
    /// weight and a random part picks the level of critical. <paramref name="weight"/> is in tenths of a pound.
    /// </summary>
    public static int CriticalMelee(GameRandom rng, int weight, int toHit, int skill, int damage, out CriticalGrade grade,
        int level = 0, bool debuffed = false)
    {
        if (debuffed) toHit += CriticalTables.MeleeDebuffToHit;
        var chance = CriticalTables.MeleeChanceWeightScale * weight + CriticalTables.MeleeChanceToHitScale * toHit
                     + CriticalTables.MeleeChanceLevelScale * level + CriticalTables.MeleeChanceToHitSkillScale * skill
                     + CriticalTables.MeleeChanceOffset;
        grade = CriticalGrade.None;
        if (rng.RandInt1(CriticalTables.MeleeChanceRange) > chance) return damage;
        var power = CriticalTables.MeleePowerWeightScale * weight + rng.RandInt1(CriticalTables.MeleePowerRandom);
        return Level(power, CriticalTables.MeleeLevels, damage, out grade);
    }

    /// <summary>Angband critical_shot (constants.txt ranged-critical), for fired and thrown missiles.</summary>
    public static int CriticalShot(GameRandom rng, int weight, int toHit, int playerLevel, int damage, out CriticalGrade grade,
        int skill = 0, bool launched = true, bool debuffed = false)
    {
        if (debuffed) toHit += CriticalTables.RangedDebuffToHit;
        var chance = CriticalTables.RangedChanceWeightScale * weight + CriticalTables.RangedChanceToHitScale * toHit
                     + CriticalTables.RangedChanceLevelScale * playerLevel + CriticalTables.RangedChanceOffset
                     + (launched ? CriticalTables.RangedChanceLaunchedToHitSkillScale : CriticalTables.RangedChanceThrownToHitSkillScale) * skill;
        grade = CriticalGrade.None;
        if (rng.RandInt1(CriticalTables.RangedChanceRange) > chance) return damage;
        var power = CriticalTables.RangedPowerWeightScale * weight + rng.RandInt1(CriticalTables.RangedPowerRandom);
        return Level(power, CriticalTables.RangedLevels, damage, out grade);
    }

    private static int Level(int power, (int Cutoff, int Multiplier, int Add, CriticalGrade Grade)[] levels, int damage, out CriticalGrade grade)
    {
        var l = 0;
        while (l < levels.Length - 1 && power >= levels[l].Cutoff) l++;
        grade = levels[l].Grade;
        return levels[l].Add + levels[l].Multiplier * damage;
    }

    /// <summary>Angband adjust_dam_armor: armour soaks up to 60% of a physical blow.</summary>
    public static int ArmourReduce(int damage, int armour) =>
        damage - damage * Math.Min(armour, MaxArmourReduction) / 400;

    /// <summary>
    /// Angband adjust_dam for elemental damage. <paramref name="resistLevel"/>: -1 vulnerable,
    /// 0 none, 1 resist, 2 double resist (permanent + temporary), 3 immune.
    /// </summary>
    public static int ResistElement(GameRandom rng, ElementDef element, int damage, int resistLevel)
    {
        if (damage <= 0) return 0;
        if (resistLevel >= 3) return 0;
        if (resistLevel < 0) return damage * 4 / 3;
        var denominator = element.Denominator.Roll(rng); // rolled once, as 4.2.5 does
        for (var i = resistLevel; i > 0; i--)
            if (denominator > 0) damage = damage * element.Numerator / denominator;
        return damage;
    }

    /// <summary>
    /// Angband monster_critical: how hard a monster blow landed, from 0 (not critical) to 6+,
    /// used for the chance and size of cuts and stuns.
    /// </summary>
    public static int MonsterCritical(GameRandom rng, Dice dice, int damage)
    {
        var total = dice.Max;
        if (damage < total * 19 / 20) return 0;
        if (damage < 20 && rng.RandInt0(100) >= damage) return 0;

        var bonus = damage == total ? 1 : 0;
        if (damage >= 20)
            while (rng.RandInt0(100) < 2) bonus++;

        return damage switch
        {
            > 45 => 6 + bonus,
            > 33 => 5 + bonus,
            > 25 => 4 + bonus,
            > 18 => 3 + bonus,
            > 11 => 2 + bonus,
            _ => 1 + bonus,
        };
    }

    /// <summary>Cut duration for a monster critical grade.</summary>
    public static int CutAmount(GameRandom rng, int grade) => grade switch
    {
        <= 0 => 0,
        1 => rng.RandInt1(5),
        2 => rng.RandInt1(5) + 5,
        3 => rng.RandInt1(20) + 20,
        4 => rng.RandInt1(50) + 50,
        5 => rng.RandInt1(100) + 100,
        6 => 300,
        _ => 500,
    };

    /// <summary>Stun amount for a monster critical grade.</summary>
    public static int StunAmount(GameRandom rng, int grade) => grade switch
    {
        <= 0 => 0,
        1 => rng.RandInt1(5),
        2 => rng.RandInt1(10) + 10,
        3 => rng.RandInt1(20) + 20,
        4 => rng.RandInt1(30) + 30,
        5 => rng.RandInt1(40) + 40,
        6 => 100,
        _ => 200,
    };

    /// <summary>
    /// Energy one melee blow costs: attacking stops once another blow would exceed a full turn, so
    /// fractional blows (e.g. 2.5 = 250) leave energy over (Angband py_attack).
    /// </summary>
    public static int BlowEnergy(int blowsTimes100, int moveEnergy) =>
        moveEnergy * 100 / Math.Max(100, blowsTimes100);

    /// <summary>Energy one shot costs; <paramref name="shotsTimes10"/> of 10 is one shot per turn.</summary>
    public static int ShotEnergy(int shotsTimes10, int moveEnergy) =>
        moveEnergy * 10 / Math.Max(1, shotsTimes10);

    /// <summary>
    /// Angband mon_take_hit fear check: a hurt monster may panic, more likely as its health drops or
    /// when a blow nearly kills it. Returns the fear duration (0 = not frightened).
    /// </summary>
    public static int MonsterFearOnHit(GameRandom rng, int damage, int hpAfter, int maxHp)
    {
        if (hpAfter <= 0 || maxHp <= 0) return 0;
        var percentage = 100 * hpAfter / maxHp;
        var bigHit = damage >= hpAfter;
        if (rng.RandInt1(10) < percentage && !(bigHit && rng.RandInt0(100) < 80)) return 0;
        return rng.RandInt1(10) + (bigHit && percentage > 7 ? 20 : (11 - percentage) * 5);
    }

    /// <summary>Angband's exponent: experience for a kill is <c>exp * monster level / player level</c>.</summary>
    public static (int Whole, int Fraction) KillExperience(int raceExp, int raceLevel, int playerLevel)
    {
        long total = (long)raceExp * raceLevel;
        var level = Math.Max(1, playerLevel);
        var whole = total / level;
        var frac = (total % level) * 0x10000L / level;
        return ((int)whole, (int)frac);
    }
}
