using System.Text;
using Angband.Core.Definitions;

namespace Angband.Core.Items;

/// <summary>
/// Builds object names as the player sees them (Angband object_desc). Names use <c>~</c> to mark
/// where the plural goes (<c>Ration~ of Food</c>, <c>Wooden Torch~</c>). Unknown flavoured objects
/// show their flavour ("a Cloudy Potion"); unknown properties are hidden and flagged <c>{??}</c>.
/// </summary>
public static class ItemNaming
{
    public static string Describe(Item item, PlayerKnowledge knowledge, bool withArticle = true, bool full = true)
    {
        if (item.IsGold) return $"{item.GoldValue} gold pieces worth of {Plain(item.Kind.Name, false)}";

        var knowsKind = knowledge.KnowsKind(item);
        var fullyKnown = knowledge.IsFullyKnown(item);
        var plural = item.Number != 1;

        string noun;
        if (!knowsKind && knowledge.Flavor(item.Kind) is { } flavor)
        {
            var pattern = item.Base.Flavor == "scroll"
                ? "{base} titled \"{flavor}\""
                : "{flavor} {base}";
            noun = pattern.Replace("{flavor}", flavor.Name).Replace("{base}", Plain(item.Base.Name, plural));
        }
        else if (item.IsFlavored && knowledge.ShowFlavors && knowledge.Flavor(item.Kind) is { } known)
        {
            // Angband show_flavors: "an Icky Green Potion of Speed", "a Scroll titled "abc" of Light".
            var pattern = item.Base.Flavor == "scroll" ? "{base} titled \"{flavor}\"" : "{flavor} {base}";
            noun = pattern.Replace("{flavor}", known.Name).Replace("{base}", Plain(item.Base.Name, plural))
                   + " of " + Plain(item.Kind.Name, false);
        }
        else if (item.IsFlavored)
            noun = Plain(item.Base.Name, plural) + " of " + Plain(item.Kind.Name, false);
        else
            noun = Plain(item.Kind.Name, plural);

        var sb = new StringBuilder();
        if (withArticle)
        {
            if (item.IsArtifact && fullyKnown) sb.Append("the ");
            else if (plural) sb.Append(item.Number).Append(' ');
            else sb.Append(StartsWithVowel(noun) ? "an " : "a ");
        }
        sb.Append(noun);

        if (fullyKnown)
        {
            if (item.Artifact is { } art) sb.Append(' ').Append(art.Name);
            else if (item.Ego is { } ego) sb.Append(' ').Append(ego.Name);
        }
        if (!full) return sb.ToString();

        // Combat numbers.
        if (item.Base.IsWeapon || item.IsAmmo)
            sb.Append($" ({item.Damage})");
        if (item.Base.Slot == EquipSlot.Bow && item.Kind.Multiplier > 0)
            sb.Append($" (x{item.Kind.Multiplier})");

        var knowsHit = knowledge.KnowsRune(RuneIds.ToHit);
        var knowsDam = knowledge.KnowsRune(RuneIds.ToDam);
        var runes = item.Runes().ToHashSet();
        if (runes.Contains(RuneIds.ToHit) || runes.Contains(RuneIds.ToDam))
        {
            if (knowsHit && knowsDam) sb.Append($" ({item.ToHit:+0;-0},{item.ToDam:+0;-0})");
            else if (knowsHit) sb.Append($" ({item.ToHit:+0;-0},+?)");
            else if (knowsDam) sb.Append($" (+?,{item.ToDam:+0;-0})");
        }

        if (item.Armour > 0 || runes.Contains(RuneIds.ToAc))
        {
            if (knowledge.KnowsRune(RuneIds.ToAc)) sb.Append($" [{item.Armour},{item.ToAc:+0;-0}]");
            else sb.Append($" [{item.Armour}]");
        }

        var knownMods = item.Modifiers.Where(kv => kv.Value != 0 && knowledge.KnowsRune(RuneIds.Modifier(kv.Key))).ToList();
        if (knownMods.Count > 0)
            sb.Append(" <").Append(string.Join(", ", knownMods.Select(kv => $"{kv.Value:+0;-0}"))).Append('>');

        if (item.Kind.Fuel > 0) sb.Append($" ({item.Fuel} turns)");
        if (item.IsChest) sb.Append(' ').Append(ChestDescription(item, knowledge));
        if (item.Base.Id is "wand" or "staff" && knowsKind)
            sb.Append(item.Charges == 1 ? " (1 charge)" : $" ({item.Charges} charges)");
        if (item.Timeout > 0 && (item.Base.Id == "rod" || item.CanActivate)) sb.Append(" (charging)");

        // Angband obj_desc_inscrip: the inscription and the special notes, in one pair of braces.
        var notes = new List<string>();
        if (!string.IsNullOrEmpty(item.Note)) notes.Add(item.Note);
        if (item.Base.Id is "wand" or "staff" && !knowsKind && item.Flags.Contains("EMPTY")) notes.Add("empty");
        if (!knowsKind && knowledge.HasTried(item.Kind)) notes.Add("tried");
        if (item.Curses.Any(c => knowledge.KnowsRune(RuneIds.Curse(c)))) notes.Add("cursed");
        if (knowledge.IgnoredCheck?.Invoke(item) == true) notes.Add("ignore");
        if (knowsKind && !fullyKnown) notes.Add("??");
        if (notes.Count > 0) sb.Append(" {").Append(string.Join(", ", notes)).Append('}');
        return sb.ToString();
    }

    /// <summary>Resolves the <c>~</c> plural marker.</summary>
    /// <summary>Angband chest_trap_name: (Empty), (Unlocked), (Disarmed), (Locked), a trap's name or (Multiple Traps).</summary>
    public static string ChestDescription(Item chest, PlayerKnowledge knowledge)
    {
        var s = chest.ChestState;
        if (s == 0) return "(Empty)";
        if (s == -1) return "(Unlocked)";
        if (s < 0) return "(Disarmed)";
        if (s == 1) return "(Locked)";
        var traps = knowledge.ChestTraps.Where(t => t.Bit > 1 && (s & t.Bit) != 0).Select(t => t.Name).Distinct().ToList();
        return traps.Count == 1 ? $"({traps[0]})" : "(Multiple Traps)";
    }

    public static string Plain(string name, bool plural)
    {
        if (!name.Contains('~')) return plural && !name.EndsWith('s') ? name + "s" : name;
        var i = name.IndexOf('~');
        if (!plural) return name.Remove(i, 1);
        var before = name[..i];
        var suffix = before.EndsWith("ch") || before.EndsWith("sh") || before.EndsWith('s') || before.EndsWith('x') ? "es" : "s";
        return before + suffix + name[(i + 1)..];
    }

    private static bool StartsWithVowel(string s) => s.Length > 0 && "aeiouAEIOU".Contains(s[0]);
}
