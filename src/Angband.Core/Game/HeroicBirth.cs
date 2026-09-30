using Angband.Core.Definitions;
using Angband.Core.Magic;
using Angband.Core.Randomness;

namespace Angband.Core.Game;

/// <summary>
/// AVABand's superlative characters (not in Angband 4.2.5): a heroic roll, a heroic point-buy, and
/// an autoroller that rerolls until every stat reaches a minimum (as Angband 3.x's did). Heroic
/// characters are scored as usual, but tagged "Heroic" in the scores and the character dump.
/// </summary>
public static class HeroicBirth
{
    /// <summary>Whether the stats came from one of the heroic methods.</summary>
    public static bool IsHeroic(this StatMethod method) => method is StatMethod.HeroicRoll or StatMethod.HeroicPointBuy;

    /// <summary>Whether the stats were bought with points (normal or heroic).</summary>
    public static bool IsPointBuy(this StatMethod method) => method is StatMethod.PointBuy or StatMethod.HeroicPointBuy;

    /// <summary>The highest base stat a heroic character can have: 18/50.</summary>
    public const int HeroicMax = 23;

    /// <summary>The heroic point-buy budget (a normal one is constants.json's birthPoints, 20).</summary>
    public const int HeroicBudget = 50;

    /// <summary>What each step above 18 costs in a heroic point-buy (18/10 ... 18/50).</summary>
    public const int StepAbove18 = 3;

    /// <summary>
    /// A heroic roll: each stat is 11 plus 3d4 — 14 to 18/50, 18/10 on average — against the
    /// ordinary roll's 8 to 17.
    /// </summary>
    public static Dictionary<string, int> RollStats(GameRandom rng)
    {
        var stats = new Dictionary<string, int>();
        foreach (var stat in CharacterSpec.StatIds) stats[stat] = 11 + rng.RandInt1(4) + rng.RandInt1(4) + rng.RandInt1(4);
        return stats;
    }

    /// <summary>Rolls stats by <paramref name="method"/> (the ordinary roll or the heroic one).</summary>
    public static Dictionary<string, int> Roll(StatMethod method, GameRandom rng) =>
        method == StatMethod.HeroicRoll ? RollStats(rng) : Birth.RollStats(rng);

    /// <summary>The lowest and highest base stat a method can give.</summary>
    public static (int Min, int Max) BaseRange(StatMethod method) => method switch
    {
        StatMethod.Roll => (8, 17),
        StatMethod.HeroicRoll => (14, HeroicMax),
        StatMethod.HeroicPointBuy => (Birth.PointBuyMin, HeroicMax),
        _ => (Birth.PointBuyMin, Birth.PointBuyMax),
    };

    /// <summary>The point-buy budget for a method.</summary>
    public static int Budget(StatMethod method, GameConstants constants) =>
        method == StatMethod.HeroicPointBuy ? HeroicBudget : constants.BirthPoints;

    /// <summary>What a stat costs to buy up from 10: Angband's costs to 18, then 3 a step to 18/50.</summary>
    public static int Cost(int stat) => Birth.Cost(stat) + Math.Max(0, Math.Min(stat, HeroicMax) - Birth.PointBuyMax) * StepAbove18;

    public static int PointsSpent(StatMethod method, IReadOnlyDictionary<string, int> stats) =>
        method == StatMethod.HeroicPointBuy ? stats.Values.Sum(Cost) : Birth.PointsSpent(stats);

    /// <summary>Checks a point-buy of either kind; null if fine, otherwise the problem.</summary>
    public static string? ValidatePointBuy(StatMethod method, IReadOnlyDictionary<string, int> stats, GameConstants constants)
    {
        if (method != StatMethod.HeroicPointBuy) return Birth.ValidatePointBuy(stats, constants.BirthPoints);
        if (CharacterSpec.StatIds.Any(s => !stats.ContainsKey(s))) return "Every stat needs a value.";
        if (stats.Values.Any(v => v is < Birth.PointBuyMin or > HeroicMax))
            return $"Stats must be between {Birth.PointBuyMin} and {StatTables.Format(HeroicMax)}.";
        var spent = PointsSpent(method, stats);
        return spent > HeroicBudget ? $"That costs {spent} points; you only have {HeroicBudget}." : null;
    }

    /// <summary>
    /// Why some minimums can never be met by <paramref name="method"/>'s rolls for this race and
    /// class (the stat named, and its best), or null if each is within reach. Minimums are on the
    /// final stats, after race and class.
    /// </summary>
    public static string? Unreachable(StatMethod method, IReadOnlyDictionary<string, int> minimums, RaceDef? race, ClassDef? cls)
    {
        var max = BaseRange(method).Max;
        foreach (var stat in CharacterSpec.StatIds)
        {
            if (!minimums.TryGetValue(stat, out var want) || want <= 0) continue;
            var best = Birth.FinalStat(max, race, cls, stat);
            if (want > best) return $"{stat.ToUpperInvariant()} can't reach {StatTables.Format(want)} with this roll (its best is {StatTables.Format(best)}).";
        }
        return null;
    }

    /// <summary>
    /// The autoroller: rolls up to <paramref name="maxRolls"/> sets until one meets every minimum
    /// (on the final stats). Returns the set and how many rolls it took, or null if none did.
    /// </summary>
    public static (Dictionary<string, int> Stats, int Rolls)? AutoRoll(StatMethod method, IReadOnlyDictionary<string, int> minimums,
        RaceDef? race, ClassDef? cls, GameRandom rng, int maxRolls = 100_000)
    {
        for (var n = 1; n <= maxRolls; n++)
        {
            var stats = Roll(method, rng);
            if (CharacterSpec.StatIds.All(s => !minimums.TryGetValue(s, out var want) || Birth.FinalStat(stats[s], race, cls, s) >= want))
                return (stats, n);
        }
        return null;
    }
}
