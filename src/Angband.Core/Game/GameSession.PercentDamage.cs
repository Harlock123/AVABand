using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Monsters;

namespace Angband.Core.Game;

// Angband 4.2's birth_percent_damage, "To-damage is a percentage of dice" (O-combat, from
// Oangband): to-dam no longer adds to each blow but makes every damage die bigger by a percentage
// (the deadliness table); slays and brands multiply the dice by their own O-multipliers; a critical
// adds whole dice rather than multiplying; and everyone has at least two blows.
// player-attack.c o_melee_damage / o_ranged_damage / o_critical_melee / o_critical_shot.
public sealed partial class GameSession
{
    /// <summary>Whether this character uses 4.2's percentage damage (O-combat).</summary>
    public bool PercentDamage => Options[OptionIds.PercentDamage];

    /// <summary>Angband deadliness_conversion: to-dam 0..150 as a percentage added to the dice.</summary>
    private static readonly int[] DeadlinessConversion =
    [
        0,
        5, 10, 14, 18, 22, 26, 30, 33, 36, 39,
        42, 45, 48, 51, 54, 57, 60, 63, 66, 69,
        72, 75, 78, 81, 84, 87, 90, 93, 96, 99,
        102, 104, 107, 109, 112, 114, 117, 119, 122, 124,
        127, 129, 132, 134, 137, 139, 142, 144, 147, 149,
        152, 154, 157, 159, 162, 164, 167, 169, 172, 174,
        176, 178, 180, 182, 184, 186, 188, 190, 192, 194,
        196, 198, 200, 202, 204, 206, 208, 210, 212, 214,
        216, 218, 220, 222, 224, 226, 228, 230, 232, 234,
        236, 238, 240, 242, 244, 246, 248, 250, 251, 253,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
    ];

    /// <summary>A slay's O-multiplier, in tenths (slay.txt o-multiplier): evil ×1.8, animals ×2, the ×3 slays ×2.5...</summary>
    internal int OSlayMultiplier(string monsterFlag, int multiplier) =>
        Data.SlayType(monsterFlag, multiplier)?.OMultiplier ?? 10 * multiplier;

    /// <summary>
    /// A brand's O-multiplier, in tenths (brand.txt o-multiplier): ×1.5 for the weak brands, ×2.5 for
    /// the others; a vulnerable monster takes twice the extra (get_monster_brand_multiplier).
    /// </summary>
    internal int OBrandMultiplier(string element, int multiplier, bool vulnerable)
    {
        var o = Data.BrandType(element, multiplier)?.OMultiplier ?? 10 * multiplier;
        return vulnerable ? 2 * (o - 10) + 10 : o;
    }

    /// <summary>Angband is_debuffed: a confused, held, frightened or stunned monster is easier to crit.</summary>
    internal static bool IsDebuffed(Monster monster) =>
        monster.Confused > 0 || monster.Held > 0 || monster.Fear > 0 || monster.Stun > 0;

    /// <summary>
    /// The heart of O-combat: <paramref name="dice"/> dice whose average, <c>(sides + 1) / 2</c>, is
    /// scaled by the multiplier (tenths) and by deadliness, then rolled; a slay or brand also adds
    /// its extra tenths (<c>add</c>).
    /// </summary>
    internal int ODamage(int dice, int sides, int launcherMultiplier, int oMultiplier, int deadliness, int extraDice)
    {
        var dieAverage = 10 * (sides + 1) / 2 * Math.Max(1, launcherMultiplier);
        dieAverage *= oMultiplier;
        var add = oMultiplier > 10 ? oMultiplier - 10 : 0;

        // apply_deadliness (100x inflation).
        deadliness = Math.Clamp(deadliness, -150, 150);
        if (deadliness >= 0) dieAverage *= 100 + DeadlinessConversion[deadliness];
        else if (DeadlinessConversion[-deadliness] is var cut && cut >= 100) dieAverage = 0;
        else dieAverage *= 100 - DeadlinessConversion[-deadliness];

        // Sides from the average: 2 × average − 1, with the fraction as a chance of one more.
        var scaled = 2 * dieAverage - 10000;
        var extra = Rng.RandInt0(10000) < scaled % 10000;
        var dieSides = scaled / 10000 + (extra ? 1 : 0);
        return (dieSides > 0 ? Rng.Damroll(dice + extraDice, dieSides) : 0) + add;
    }

    /// <summary>Angband o_critical_melee: the extra dice a blow's critical adds.</summary>
    private int OCriticalMelee(Monster monster, int toHit, out CriticalGrade grade)
    {
        // constants.txt o-melee-critical: power from the hit chance, a third of it, against power + 240.
        var power = CombatMath.MeleeChance(Player.SkillMelee, toHit) + (IsDebuffed(monster) ? CriticalTables.OMeleeDebuffToHit : 0);
        power = power * CriticalTables.OMeleePowerToHitScaleNumerator / CriticalTables.OMeleePowerToHitScaleDenominator;
        power = power * CriticalTables.OMeleeChancePowerScaleNumerator / CriticalTables.OMeleeChancePowerScaleDenominator;
        return OCritical(power, CriticalTables.OMeleeChanceAddDenominator, CriticalTables.OMeleeLevels, out grade);
    }

    /// <summary>Angband o_critical_shot: fired missiles at full power, thrown weapons at half again.</summary>
    private int OCriticalShot(Monster monster, int skill, int toHit, bool launched, out CriticalGrade grade)
    {
        // constants.txt o-ranged-critical: fired missiles at full power, thrown weapons at half again.
        var power = CombatMath.MeleeChance(skill, toHit) + (IsDebuffed(monster) ? CriticalTables.ORangedDebuffToHit : 0);
        power = launched
            ? power * CriticalTables.ORangedPowerLaunchedToHitScaleNumerator / CriticalTables.ORangedPowerLaunchedToHitScaleDenominator
            : power * CriticalTables.ORangedPowerThrownToHitScaleNumerator / CriticalTables.ORangedPowerThrownToHitScaleDenominator;
        power = power * CriticalTables.ORangedChancePowerScaleNumerator / CriticalTables.ORangedChancePowerScaleDenominator;
        return OCritical(power, CriticalTables.ORangedChanceAddDenominator, CriticalTables.ORangedLevels, out grade);
    }

    private int OCritical(int power, int add, (int Chance, int Dice, CriticalGrade Grade)[] levels, out CriticalGrade grade)
    {
        grade = CriticalGrade.None;
        if (power <= 0 || Rng.RandInt1(power + add) > power) return 0;
        var level = 0;
        while (level < levels.Length - 1 && !Rng.OneIn(levels[level].Chance)) level++;
        grade = levels[level].Grade;
        return levels[level].Dice;
    }

    /// <summary>Angband o_melee_damage for one blow (bare hands are a 1d1 "weapon" without criticals).</summary>
    private int OMeleeDamage(Monster monster, Item? weapon, int oMultiplier, int toHit, out CriticalGrade grade)
    {
        grade = CriticalGrade.None;
        var dice = weapon?.Damage.Count ?? 1;
        var sides = weapon?.Damage.Sides ?? 1;
        var deadliness = Math.Min((weapon?.ToDam ?? 0) + Player.EffectiveToDam, 150);
        var critDice = weapon is null ? 0 : OCriticalMelee(monster, toHit, out grade);
        return ODamage(dice, sides, 1, oMultiplier, deadliness, critDice);
    }

    /// <summary>
    /// Angband o_ranged_damage: fired ammunition takes the launcher's multiplier and every to-dam;
    /// a thrown throwing weapon, the player's to-dam too and (2 + weight/12) times the dice.
    /// </summary>
    private int ORangedDamage(Monster monster, Item missile, Item? launcher, int launcherMultiplier, int oMultiplier,
        int skill, int toHit, out CriticalGrade grade)
    {
        grade = CriticalGrade.None;
        var deadliness = launcher is not null ? missile.ToDam + launcher.ToDam + Player.EffectiveToDam
            : missile.IsThrowing ? missile.ToDam + Player.EffectiveToDam
            : missile.ToDam;
        var dice = missile.Damage.Count;
        if (launcher is not null) dice += OCriticalShot(monster, skill, toHit, launched: true, out grade);
        else if (missile.IsThrowing)
        {
            dice += OCriticalShot(monster, skill, toHit, launched: false, out grade);
            dice *= ThrowMultiplier(missile);
        }
        return ODamage(dice, missile.Damage.Sides, launcher is null ? 1 : launcherMultiplier, oMultiplier,
            Math.Min(deadliness, 150), 0);
    }
}
