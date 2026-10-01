using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>A set of stones in one pair of bracers, and what it adds beyond the stones themselves.</summary>
public sealed record GemSetBonus(string Text, string? Immunity = null, string? Resist = null, string? Stat = null, int StatBonus = 0,
    int Armour = 0, int Infravision = 0);

// AVABand's gem sets: stones set together in one pair of worn bracers answer one another. Three of a
// kind (any quality: chipped, flawed or flawless) give a stronger form of their virtue — three rubies
// immunity to fire, three sapphires to cold, three topazes to lightning; three emeralds, garnets or
// amethysts two more CON, STR or INT; three diamonds ten more armour; three opals three more
// infravision — and a ruby, a sapphire and a topaz together resist acid too (the fourth base
// element). Three sockets are needed for a set: mithril bracers, or Celebrimbor's iron ones.
public sealed partial class GameSession
{
    private static readonly IReadOnlyDictionary<string, GemSetBonus> ThreeOfAKind = new Dictionary<string, GemSetBonus>
    {
        ["ruby"] = new("three rubies: immunity to fire", Immunity: "fire"),
        ["sapphire"] = new("three sapphires: immunity to cold", Immunity: "cold"),
        ["topaz"] = new("three topazes: immunity to lightning", Immunity: "elec"),
        ["emerald"] = new("three emeralds: +2 CON", Stat: "con", StatBonus: 2),
        ["garnet"] = new("three garnets: +2 STR", Stat: "str", StatBonus: 2),
        ["amethyst"] = new("three amethysts: +2 INT", Stat: "int", StatBonus: 2),
        ["diamond"] = new("three diamonds: +10 armour", Armour: 10),
        ["opal"] = new("three opals: +3 infravision", Infravision: 3),
    };

    private static readonly GemSetBonus Trinity = new("a ruby, a sapphire and a topaz: resist acid", Resist: "acid");

    /// <summary>A gem's family: its kind without its quality ("flawed_ruby" is a ruby).</summary>
    public static string GemFamily(Item gem) =>
        gem.Kind.Id.StartsWith("chipped_", StringComparison.Ordinal) ? gem.Kind.Id[8..]
        : gem.Kind.Id.StartsWith("flawed_", StringComparison.Ordinal) ? gem.Kind.Id[7..] : gem.Kind.Id;

    /// <summary>The sets the stones in these bracers make.</summary>
    public static IReadOnlyList<GemSetBonus> GemSets(Item bracers)
    {
        if (bracers.Gems.Count < 3) return [];
        var families = bracers.Gems.Select(GemFamily).ToList();
        var sets = families.GroupBy(f => f).Where(g => g.Count() >= 3 && ThreeOfAKind.ContainsKey(g.Key))
            .Select(g => ThreeOfAKind[g.Key]).ToList();
        if (families.Contains("ruby") && families.Contains("sapphire") && families.Contains("topaz")) sets.Add(Trinity);
        return sets;
    }

    /// <summary>The sets in the bracers you wear (they count only worn).</summary>
    private List<GemSetBonus> WornGemSets() => [.. Player.Inventory.Equipped.Where(i => i.Gems.Count >= 3).SelectMany(GemSets)];
}
