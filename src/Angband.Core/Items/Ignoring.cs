using Angband.Core.Definitions;

namespace Angband.Core.Items;

/// <summary>Quality levels for ignoring (Angband IGNORE_NONE ... IGNORE_ALL; MAX = can't tell yet).</summary>
public enum IgnoreLevel
{
    None,
    Bad,
    Average,
    Good,
    /// <summary>Everything but artifacts ("non-artifact").</summary>
    All,
    /// <summary>Not yet known well enough to judge (or an artifact): never ignored by quality.</summary>
    Unknown,
}

/// <summary>An ignore group of wearable items (Angband list-ignore-types.h).</summary>
public sealed record IgnoreType(string Id, string Name);

/// <summary>
/// Angband's item ignoring (obj-ignore.c): single items, whole kinds (flavours), egos per item type,
/// and a quality threshold for each type of equipment.
/// </summary>
public static class Ignoring
{
    public static readonly IReadOnlyList<IgnoreType> Types =
    [
        new("sharp", "Sharp Melee Weapons"), new("blunt", "Blunt Melee Weapons"), new("great", "Great Weapons"),
        new("sling", "Slings"), new("bow", "Bows"), new("crossbow", "Crossbows"),
        new("shot", "Shots and Pebbles"), new("arrow", "Arrows"), new("bolt", "Bolts"),
        new("robe", "Robes"), new("body_armour", "Body Armor"),
        new("basic_dragon", "Basic Dragon Scale Mail"), new("multi_dragon", "Multi-Hued Dragon Scale Mail"),
        new("high_dragon", "High Dragon Scale Mail"), new("balance_dragon", "Balance Dragon Scale Mail"),
        new("power_dragon", "Power Dragon Scale Mail"),
        new("cloak", "Cloaks"), new("elven_cloak", "Elven Cloaks"), new("shield", "Shields"),
        new("headgear", "Headgear"), new("handgear", "Handgear"), new("feet", "Footgear"), new("digger", "Diggers"),
        new("ring", "Rings"), new("amulet", "Amulets"), new("light", "Lights"),
    ];

    public static readonly IReadOnlyDictionary<IgnoreLevel, string> LevelNames = new Dictionary<IgnoreLevel, string>
    {
        [IgnoreLevel.None] = "no ignore", [IgnoreLevel.Bad] = "bad", [IgnoreLevel.Average] = "average",
        [IgnoreLevel.Good] = "good", [IgnoreLevel.All] = "non-artifact",
    };

    // Angband quality_mapping: (base, identifier in the kind's name, type); identified entries first.
    private static readonly (string Base, string Identifier, string Type)[] Mapping =
    [
        ("sword", "Chaos", "great"), ("polearm", "Slicing", "great"), ("hafted", "Disruption", "great"),
        ("sword", "", "sharp"), ("polearm", "", "sharp"), ("hafted", "", "blunt"),
        ("sling", "", "sling"), ("bow", "", "bow"), ("crossbow", "", "crossbow"),
        ("shot", "", "shot"), ("arrow", "", "arrow"), ("bolt", "", "bolt"),
        ("soft_armour", "Robe", "robe"),
        ("dragon_armour", "Black", "basic_dragon"), ("dragon_armour", "Blue", "basic_dragon"),
        ("dragon_armour", "White", "basic_dragon"), ("dragon_armour", "Red", "basic_dragon"),
        ("dragon_armour", "Green", "basic_dragon"), ("dragon_armour", "Multi", "multi_dragon"),
        ("dragon_armour", "Shining", "high_dragon"), ("dragon_armour", "Law", "high_dragon"),
        ("dragon_armour", "Gold", "high_dragon"), ("dragon_armour", "Chaos", "high_dragon"),
        ("dragon_armour", "Balance", "balance_dragon"), ("dragon_armour", "Power", "power_dragon"),
        ("hard_armour", "", "body_armour"), ("soft_armour", "", "body_armour"),
        ("cloak", "Elven", "elven_cloak"), ("cloak", "", "cloak"), ("shield", "", "shield"),
        ("helm", "", "headgear"), ("crown", "", "headgear"), ("gloves", "", "handgear"), ("boots", "", "feet"),
        ("digger", "", "digger"), ("ring", "", "ring"), ("amulet", "", "amulet"), ("light", "", "light"),
    ];

    /// <summary>Bases that can be ignored kind by kind (Angband sval_dependent, less gold).</summary>
    public static readonly IReadOnlySet<string> KindBases = new HashSet<string>
    {
        "staff", "wand", "rod", "scroll", "potion", "ring", "amulet", "food", "mushroom",
        "magic_book", "prayer_book", "nature_book", "shadow_book", "light", "flask",
    };

    /// <summary>Angband ignore_type_of: the item's ignore group, or null if it has none.</summary>
    public static IgnoreType? TypeOf(ObjectKindDef kind)
    {
        foreach (var (b, identifier, type) in Mapping)
            if (b == kind.Base && (identifier.Length == 0 || kind.Name.Contains(identifier, StringComparison.Ordinal)))
                return Types.First(t => t.Id == type);
        return null;
    }

    /// <summary>Angband cmp_object_trait: a bonus against the kind's (a positive base counts as zero).</summary>
    private static int Compare(int bonus, int kindBase) => bonus.CompareTo(Math.Min(kindBase, 0));

    /// <summary>
    /// Angband ignore_level_of: how good the item is known to be. Jewellery is only ever bad or
    /// average; others are bad, average, good or "all" (an ego) once fully known; an item that has
    /// been looked over but still has unknown runes counts as "all" (it's no artifact); anything else
    /// (unseen properties, artifacts) is Unknown and never ignored by quality.
    /// </summary>
    public static IgnoreLevel LevelOf(Item item, PlayerKnowledge knowledge)
    {
        if (item.Base.Id is "ring" or "amulet")
        {
            var knownMods = item.Modifiers.Where(kv => knowledge.KnowsRune(RuneIds.Modifier(kv.Key))).Select(kv => kv.Value).ToList();
            if (knownMods.Any(v => v > 0)) return IgnoreLevel.Average;
            int Known(int v, string rune) => knowledge.KnowsRune(rune) ? v : 0;
            var h = Known(item.ToHit, RuneIds.ToHit);
            var d = Known(item.ToDam, RuneIds.ToDam);
            var a = Known(item.ToAc, RuneIds.ToAc);
            if (h > 0 || d > 0 || a > 0) return IgnoreLevel.Average;
            if (h < 0 || d < 0 || a < 0 || knownMods.Any(v => v < 0)) return IgnoreLevel.Bad;
            return IgnoreLevel.Average;
        }
        if (knowledge.IsFullyKnown(item))
        {
            if (item.IsArtifact) return IgnoreLevel.Unknown;
            if (item.Ego is not null) return IgnoreLevel.All;
            var good = 4 * Compare(item.ToDam, item.Kind.ToDam) + 2 * Compare(item.ToHit, item.Kind.ToHit) + Compare(item.ToAc, item.Kind.ToAc);
            return good > 0 ? IgnoreLevel.Good : good < 0 ? IgnoreLevel.Bad : IgnoreLevel.Average;
        }
        return item.Assessed && !item.IsArtifact ? IgnoreLevel.All : IgnoreLevel.Unknown;
    }
}

/// <summary>A character's ignore settings (saved with the game, as in Angband).</summary>
public sealed class IgnoreSettings
{
    /// <summary>Kinds ignored once identified / while still unidentified (Angband IGNORE_IF_AWARE / _UNAWARE).</summary>
    public HashSet<string> KindsAware { get; } = new(StringComparer.Ordinal);
    public HashSet<string> KindsUnaware { get; } = new(StringComparer.Ordinal);
    /// <summary>Egos ignored for an item type, as "ego|type".</summary>
    public HashSet<string> Egos { get; } = new(StringComparer.Ordinal);
    /// <summary>Quality ignored per item type (at or below it).</summary>
    public Dictionary<string, IgnoreLevel> Quality { get; } = new(StringComparer.Ordinal);

    public IgnoreLevel QualityFor(string type) => Quality.GetValueOrDefault(type, IgnoreLevel.None);

    public static string EgoKey(string egoId, string typeId) => egoId + "|" + typeId;

    /// <summary>
    /// Angband object_is_ignored: marked by hand; otherwise (not for artifacts, nor anything
    /// inscribed !k or !*) by kind, by ego, or by quality.
    /// </summary>
    public bool IsIgnored(Item item, PlayerKnowledge knowledge)
    {
        if (item.Ignored) return true;
        if (item.IsArtifact || Inscription.Has(item, "!k") || Inscription.Has(item, "!*")) return false;
        if (item.IsGold) return false;
        if ((knowledge.KnowsKind(item) ? KindsAware : KindsUnaware).Contains(item.Kind.Id)) return true;
        if (Ignoring.TypeOf(item.Kind) is not { } type) return false;
        if (item.Ego is { } ego && knowledge.IsFullyKnown(item) && Egos.Contains(EgoKey(ego.Id, type.Id))) return true;
        var threshold = QualityFor(type.Id);
        if (threshold == IgnoreLevel.None) return false;
        if (item.Assessed && !item.IsArtifact && threshold == IgnoreLevel.All) return true;
        return Ignoring.LevelOf(item, knowledge) <= threshold;
    }
}
