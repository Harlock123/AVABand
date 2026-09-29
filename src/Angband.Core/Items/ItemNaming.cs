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

        // Angband object_is_known_artifact: an artifact is known for what it is once you've stood on it
        // (object_touch) — named then, runes or no.
        var artifactKnown = item.IsArtifact && (item.Assessed || fullyKnown);

        string noun;
        // Angband obj_desc_get_basename: a special artifact's own kind (the One Ring's, the
        // Elfstone's) shows its fixed flavour until you know the kind or the artifact.
        if (item.Kind.IsSpecialArtifactKind && !artifactKnown && !knowledge.IsAware(item.Kind)
            && knowledge.Flavor(item.Kind) is { } stone)
            noun = $"{stone.Name} {Plain(item.Base.Name, plural)}";
        else if (!knowsKind && knowledge.Flavor(item.Kind) is { } flavor)
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
            if (artifactKnown) sb.Append("the ");
            else if (plural) sb.Append(item.Number).Append(' ');
            else sb.Append(StartsWithVowel(noun) ? "an " : "a ");
        }
        sb.Append(noun);

        if (artifactKnown && item.Artifact is { } art) sb.Append(' ').Append(art.Name);
        else if (fullyKnown && item.Ego is { } ego) sb.Append(' ').Append(ego.Name);
        if (!full) return sb.ToString();

        // Combat numbers.
        if (item.Base.IsWeapon || item.IsAmmo)
            sb.Append($" ({item.Damage})");
        if (item.Base.Slot == EquipSlot.Bow && item.Multiplier > 0)
            sb.Append($" (x{item.Multiplier})");

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

        if (item.UsesFuel) sb.Append($" ({item.Fuel} turns)");
        if (item.IsChest) sb.Append(' ').Append(ChestDescription(item, knowledge));
        if (item.Base.Id is "wand" or "staff" && knowsKind)
            sb.Append(item.Charges == 1 ? " (1 charge)" : $" ({item.Charges} charges)");
        // Angband obj_desc_charges: a stack of rods says how many are charging.
        if (item.Timeout > 0 && item.IsRod && item.Number > 1) sb.Append($" ({item.NumberCharging} charging)");
        else if (item.Timeout > 0 && (item.IsRod || item.CanActivate)) sb.Append(" (charging)");

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

    /// <summary>
    /// Angband chest_trap_name: (empty), (unlocked), (disarmed), (locked), a trap's name or
    /// (multiple traps) — chest_trap.txt's names, in lower case as 4.2.5 has them.
    /// </summary>
    public static string ChestDescription(Item chest, PlayerKnowledge knowledge)
    {
        var s = chest.ChestState;
        if (s == 0) return "(empty)";
        if (s == -1) return "(unlocked)";
        if (s < 0) return "(disarmed)";
        var traps = knowledge.ChestTraps.Where(t => (s & t.Bit) != 0).Select(t => t.Name).Distinct().ToList();
        if (traps.Count > 1) traps.Remove(knowledge.ChestTraps.FirstOrDefault(t => t.Bit == 1)?.Name ?? "");
        return traps.Count switch { 0 => "(locked)", 1 => $"({traps[0]})", _ => "(multiple traps)" };
    }

    /// <summary>Resolves the <c>~</c> plural marker.</summary>

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
