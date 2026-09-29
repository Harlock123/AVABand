using Angband.Core.Definitions;
using Angband.Core.Randomness;

namespace Angband.Core.Game;

/// <summary>How the stats were chosen.</summary>
public enum StatMethod { PointBuy, Roll }

/// <summary>
/// Everything chosen at character creation. <see cref="BaseStats"/> are before race and class
/// adjustments (Angband's birth screen shows both).
/// </summary>
public sealed record CharacterSpec(
    string Name,
    string RaceId,
    string ClassId,
    IReadOnlyDictionary<string, int> BaseStats,
    StatMethod Method = StatMethod.PointBuy,
    IReadOnlyDictionary<string, bool>? Options = null)
{
    public static readonly string[] StatIds = ["str", "int", "wis", "dex", "con"];

    /// <summary>A plain character: every stat 15, which is what quick starts use.</summary>
    public static CharacterSpec Default(string raceId, string classId, string name = "Adventurer") =>
        new(name, raceId, classId, StatIds.ToDictionary(s => s, _ => 15));
}

/// <summary>Angband 4.2 birth rules: point-buy costs, rolled stats and final stat calculation.</summary>
public static class Birth
{
    /// <summary>AVABand's resist-list ids and race flags as Angband's object flags.</summary>
    private static readonly Dictionary<string, string> ObjectFlagCodes = new(StringComparer.Ordinal)
    {
        ["fear"] = "PROT_FEAR", ["blind"] = "PROT_BLIND", ["conf"] = "PROT_CONF", ["stun"] = "PROT_STUN",
        ["see_invis"] = "SEE_INVIS", ["free_act"] = "FREE_ACT", ["hold_life"] = "HOLD_LIFE", ["REGENERATE"] = "REGEN",
    };

    /// <summary>
    /// A race's or class's abilities, as Angband's birth screen lists them (race_help, class_help):
    /// player_property.txt in order — object flags and player flags it has, and (races only) each
    /// element it resists as "Poison Resistance", described "You resist poison."
    /// </summary>
    public static IReadOnlyList<(string Name, string Description)> Abilities(GameData data, RaceDef? race, ClassDef? cls)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in (race?.Resists ?? []).Concat(race?.Flags ?? []).Concat(cls?.Flags ?? []))
            codes.Add(ObjectFlagCodes.TryGetValue(id, out var code) ? code : id.StartsWith("sust_") ? id.ToUpperInvariant() : id);
        // A class without magic is Angband's NO_MANA (AVABand's classes say so by having no realm).
        if (cls is { Realm: null }) codes.Add("NO_MANA");
        var list = new List<(string, string)>();
        foreach (var p in data.PlayerProperties)
        {
            if (p.Type == "element")
            {
                if (race is null) continue;
                foreach (var element in data.Elements)
                {
                    var level = race.Resists.Contains("im_" + element.Id) ? 3 : race.Resists.Contains(element.Id) ? 1 : 0;
                    if (level != p.Value || level == 0) continue;
                    list.Add(($"{char.ToUpperInvariant(element.Name[0])}{element.Name[1..]} {p.Name}", $"{p.Desc} {element.Name}."));
                }
            }
            else if (p.Code is { } code && codes.Contains(code))
                list.Add((p.Name, p.Desc));
        }
        return list;
    }

    /// <summary>Lowest and highest base stat point-buy allows.</summary>
    public const int PointBuyMin = 10, PointBuyMax = 18;

    /// <summary>
    /// Cost of each step up from 10; cumulatively this is Angband's birth_stat_costs
    /// (11→1, 12→2 ... 16→6, 17→8, 18→12 points).
    /// </summary>
    private static readonly int[] StepCost = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 4];

    /// <summary>Total cost of buying a stat up from 10.</summary>
    public static int Cost(int stat)
    {
        var total = 0;
        for (var v = PointBuyMin + 1; v <= Math.Min(stat, PointBuyMax); v++) total += StepCost[v];
        return total;
    }

    public static int PointsSpent(IReadOnlyDictionary<string, int> stats) => stats.Values.Sum(Cost);

    /// <summary>Checks a point-buy allocation; returns null if fine, otherwise the problem.</summary>
    public static string? ValidatePointBuy(IReadOnlyDictionary<string, int> stats, int budget)
    {
        if (CharacterSpec.StatIds.Any(s => !stats.ContainsKey(s))) return "Every stat needs a value.";
        if (stats.Values.Any(v => v is < PointBuyMin or > PointBuyMax)) return $"Stats must be between {PointBuyMin} and {PointBuyMax}.";
        var spent = PointsSpent(stats);
        return spent > budget ? $"That costs {spent} points; you only have {budget}." : null;
    }

    /// <summary>
    /// Angband's rolled stats: fifteen dice (1d3, 1d4, 1d5 per stat) rerolled until their total is
    /// reasonable, each stat being 5 plus its three dice (8 to 17).
    /// </summary>
    public static Dictionary<string, int> RollStats(GameRandom rng)
    {
        var dice = new int[15];
        int total;
        do
        {
            total = 0;
            for (var i = 0; i < dice.Length; i++) total += dice[i] = rng.RandInt1(3 + i % 3);
        } while (total <= 7 * 5 || total >= 9 * 5);

        var stats = new Dictionary<string, int>();
        for (var s = 0; s < 5; s++) stats[CharacterSpec.StatIds[s]] = 5 + dice[3 * s] + dice[3 * s + 1] + dice[3 * s + 2];
        return stats;
    }

    /// <summary>Base stat plus race and class adjustments, kept within 3 .. 18/220.</summary>
    public static int FinalStat(int baseValue, RaceDef? race, ClassDef? cls, string stat) =>
        Math.Clamp(baseValue + (race?.Stats.GetValueOrDefault(stat) ?? 0) + (cls?.Stats.GetValueOrDefault(stat) ?? 0), 3, 40);

    /// <summary>Gold at the start: the base amount plus 50 per unspent point.</summary>
    public static int StartingGold(CharacterSpec spec, GameConstants constants) =>
        constants.StartGold + (spec.Method == StatMethod.PointBuy
            ? Math.Max(0, constants.BirthPoints - PointsSpent(spec.BaseStats)) * 50
            : 0);
}
