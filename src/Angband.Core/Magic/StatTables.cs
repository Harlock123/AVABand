namespace Angband.Core.Magic;

/// <summary>
/// Stat-dependent magic tables, indexed like Angband's (index 0 = stat 3 ... 15 = 18, then one step
/// per 10 points of 18/xx up to 18/220). Shaped after Angband's adj_mag_mana, adj_mag_fail and
/// adj_mag_stat; the exact figures are AVABand's own.
/// </summary>
public static class StatTables
{
    /// <summary>Spell points gained per caster level, x100.</summary>
    public static readonly int[] ManaPerLevel = [0, 0, 0, 0, 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230, 240, 250, 260, 270, 280, 290, 300, 310, 320, 330];

    /// <summary>Lowest failure % a spell can have.</summary>
    public static readonly int[] MinimumFail = [99, 99, 99, 99, 99, 50, 30, 20, 15, 12, 11, 10, 9, 8, 7, 6, 6, 5, 5, 5, 4, 4, 4, 4, 3, 3, 2, 2, 2, 2, 1, 1, 1, 1, 1, 0, 0, 0];

    /// <summary>Failure % taken off every spell.</summary>
    public static readonly int[] FailReduction = [-5, -4, -3, -3, -2, -1, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 3, 4, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20];

    /// <summary>
    /// Experience needed to reach level 2, 3, ... 50 (Angband player_exp), before the race/class
    /// experience factor.
    /// </summary>
    public static readonly int[] ExperienceForLevel = [10, 25, 45, 70, 100, 140, 200, 280, 380, 500, 650, 850, 1100, 1400, 1800, 2300, 2900, 3600, 4400, 5400, 6800, 8400, 10200, 12500, 17500, 25000, 35000, 50000, 75000, 100000, 150000, 200000, 275000, 350000, 450000, 550000, 700000, 850000, 1000000, 1250000, 1500000, 1800000, 2100000, 2400000, 2700000, 3000000, 3500000, 4000000, 4500000, 5000000];

    /// <summary>To-damage bonus from Strength (Angband adj_str_td).</summary>
    public static readonly int[] ToDamage = [-2, -2, -1, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 2, 2, 3, 3, 3, 4, 4, 5, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20];

    /// <summary>Weight limit from Strength in pounds (Angband adj_str_wgt x10); half of it can be carried unhindered.</summary>
    public static readonly int[] CarryLimit = [50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 150, 160, 170, 180, 190, 200, 220, 240, 260, 280, 300, 300, 300, 300, 300, 300, 300, 300, 300, 300, 300, 300, 300, 300, 300, 300, 300];

    /// <summary>
    /// AVABand's weight limit from Strength (the birth option birth_ava_burden): Angband's up to STR 13,
    /// then 10 lb more from 14 (Angband stalls at 13-14), 20 lb a step through the 18/xx range to
    /// 18/100, and 10 lb a step beyond — Angband's stops at 18/70.
    /// </summary>
    public static readonly int[] AvaCarryLimit = [50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 220, 240, 260, 280, 300, 320, 340, 360, 380, 400, 410, 420, 430, 440, 450, 460, 470, 480, 490, 500, 510, 520];

    /// <summary>Angband adj_str_hold: the heaviest weapon (in pounds) a Strength can wield without penalty.</summary>
    public static readonly int[] StrHold = [4, 5, 6, 7, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 30, 35, 40, 45, 50, 55, 60, 65, 70, 80, 80, 80, 80, 80, 90, 90, 90, 90, 90, 100, 100, 100];

    /// <summary>Angband adj_dex_blow: Dexterity's index into the blows table.</summary>
    public static readonly int[] DexBlow = [0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 8, 9, 9, 9, 10, 10, 11, 11, 11];

    /// <summary>Angband blows_table[P][D]: the energy one blow costs, by Strength-for-weight (P) and Dexterity (D).</summary>
    public static readonly int[,] BlowsTable =
    {
        { 100, 100, 95, 85, 75, 60, 50, 42, 35, 30, 25, 23 }, { 100, 95, 85, 75, 60, 50, 42, 35, 30, 25, 23, 21 },
        { 95, 85, 75, 60, 50, 42, 35, 30, 26, 23, 21, 20 }, { 85, 75, 60, 50, 42, 36, 32, 28, 25, 22, 20, 19 },
        { 75, 60, 50, 42, 36, 33, 28, 25, 23, 21, 19, 18 }, { 60, 50, 42, 36, 33, 30, 27, 24, 22, 21, 19, 17 },
        { 50, 42, 36, 33, 30, 27, 25, 23, 21, 20, 18, 17 }, { 42, 36, 33, 30, 28, 26, 24, 22, 20, 19, 18, 17 },
        { 36, 33, 30, 28, 26, 24, 22, 21, 20, 19, 17, 16 }, { 35, 32, 29, 26, 24, 22, 21, 20, 19, 18, 17, 16 },
        { 34, 30, 27, 25, 23, 22, 21, 20, 19, 18, 17, 16 }, { 33, 29, 26, 24, 22, 21, 20, 19, 18, 17, 16, 15 },
    };

    /// <summary>To-hit bonus from Dexterity (Angband adj_dex_th).</summary>
    public static readonly int[] ToHit = [-3, -2, -2, -1, -1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 3, 3, 4, 4, 4, 5, 5, 6, 6, 7, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20];

    /// <summary>Armour bonus from Dexterity (Angband adj_dex_ta).</summary>
    public static readonly int[] ToArmour = [-4, -3, -2, -1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5, 5, 6, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 15, 15, 15, 15];

    /// <summary>Saving throw bonus from Wisdom (Angband adj_wis_sav).</summary>
    public static readonly int[] SavingThrow = [0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 5, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18];

    /// <summary>Hit points per level from Constitution, x100 (Angband adj_con_mhp).</summary>
    public static readonly int[] HitPointsPerLevel = [-250, -150, -100, -75, -50, -25, -10, -5, 0, 0, 0, 0, 0, 0, 25, 50, 75, 100, 150, 200, 250, 300, 350, 400, 450, 500, 550, 600, 650, 700, 750, 800, 900, 1000, 1100, 1250, 1250, 1250];

    public const int MaxLevel = 50;

    /// <summary>Stat value (3..40, where 19+ means 18/10, 18/20...) to table index.</summary>
    /// <summary>Angband adj_str_blow (throwing range, and with Angband's blows, Strength's part in them).</summary>
    public static readonly int[] StrBlow = [3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230, 240];

    public static int Index(int stat) => Math.Clamp(stat - 3, 0, ManaPerLevel.Length - 1);

    /// <summary>Displays a stat the Angband way: 3..18, then 18/10, 18/20 ... 18/220.</summary>
    public static string Format(int stat) => stat <= 18 ? stat.ToString() : $"18/{(stat - 18) * 10}";
}
