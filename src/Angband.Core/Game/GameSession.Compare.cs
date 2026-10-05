using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>A line of an item comparison: what's compared, yours, this one, and whether this one is better (1), worse (-1) or neither.</summary>
public sealed record ComparisonLine(string What, string Yours, string This, int Tone);

// AVABand's own: an item laid beside what you wear in its slot, line by line — damage a turn (with the
// blows you'd have), to-hit and to-dam, a launcher's multiplier, armour, each stat and ability either
// gives, curses and weight — the shop note's comparison in full.
public sealed partial class GameSession
{
    /// <summary>What you wear in the slot this item would go in (null for an empty slot or something not worn).</summary>
    public Item? WornInSlotOf(Item item)
    {
        if (!item.IsWearable || item.Base.Slot == EquipSlot.None) return null;
        var slot = Player.Inventory.SlotFor(item);
        return slot >= 0 ? Player.Inventory.Equipment[slot] : null;
    }

    /// <summary>The comparison, or null for something not worn, wielded or fired (or what you're wearing already).</summary>
    public IReadOnlyList<ComparisonLine>? Compare(Item item)
    {
        if (!item.IsWearable || item.Base.Slot == EquipSlot.None || Player.Inventory.Equipped.Contains(item)) return null;
        var mine = WornInSlotOf(item);
        var lines = new List<ComparisonLine>();
        void Number(string what, double yours, double theirs, string format = "0.#", bool higherIsBetter = true)
        {
            if (yours == 0 && theirs == 0) return;
            var d = theirs - yours;
            var tone = d == 0 ? 0 : (d > 0) == higherIsBetter ? 1 : -1;
            lines.Add(new ComparisonLine(what, mine is null ? "—" : yours.ToString(format, System.Globalization.CultureInfo.InvariantCulture),
                theirs.ToString(format, System.Globalization.CultureInfo.InvariantCulture), tone));
        }
        void Has(string what, bool yours, bool theirs, bool good = true)
        {
            if (!yours && !theirs) return;
            lines.Add(new ComparisonLine(what, yours ? "yes" : "no", theirs ? "yes" : "no", yours == theirs ? 0 : theirs == good ? 1 : -1));
        }

        switch (item.Base.Slot)
        {
            case EquipSlot.Weapon:
            {
                double PerTurn(Item? w) => w is null ? 0 : (w.Damage.AverageTimesTwo / 2.0 + w.ToDam + Player.ToDam) * BlowsWith(w) / 100.0;
                Number("Damage a turn", PerTurn(mine), PerTurn(item));
                Number("Blows a turn", BlowsWith(mine) / 100.0, BlowsWith(item) / 100.0);
                lines.Add(new ComparisonLine("Damage dice", mine?.Damage.ToString() ?? "—", item.Damage.ToString(), 0));
                Number("To hit", mine?.ToHit ?? 0, item.ToHit, "+0;-0;0");
                Number("To dam", mine?.ToDam ?? 0, item.ToDam, "+0;-0;0");
                break;
            }
            case EquipSlot.Bow:
                Number("Multiplier", mine?.Multiplier ?? 0, item.Multiplier);
                Number("To hit", mine?.ToHit ?? 0, item.ToHit, "+0;-0;0");
                Number("To dam a shot", mine?.ToDam ?? 0, item.ToDam, "+0;-0;0");
                break;
            default:
                Number("To hit", mine?.ToHit ?? 0, item.ToHit, "+0;-0;0");
                Number("To dam", mine?.ToDam ?? 0, item.ToDam, "+0;-0;0");
                break;
        }
        Number("Armour", mine is null ? 0 : mine.Armour + mine.ToAc, item.Armour + item.ToAc, "0");
        foreach (var mod in (mine?.Modifiers.Keys.AsEnumerable() ?? []).Concat(item.Modifiers.Keys).Distinct().Order())
            Number(Capital(ModifierWord(mod)), mine?.Modifier(mod) ?? 0, item.Modifier(mod), "+0;-0;0");
        foreach (var r in (mine?.Resists.AsEnumerable() ?? []).Concat(item.Resists).Distinct().Order())
            Has(Capital(AbilityWord(r)), mine?.Resists.Contains(r) == true, item.Resists.Contains(r));
        Has("Cursed", mine?.IsCursed == true, item.IsCursed, good: false);
        Number("Weight (lb)", (mine?.Weight ?? 0) / 10.0, item.Weight / 10.0, "0.0", higherIsBetter: false);
        return lines;
    }

    /// <summary>The comparison as text: "Damage a turn: 7 → 11", a line each, better marked +, worse −.</summary>
    public string CompareText(Item item)
    {
        if (Compare(item) is not { } lines) return "";
        var mine = WornInSlotOf(item);
        var head = mine is null
            ? $"Against your empty {SlotWord(item.Base.Slot)} slot:"
            : $"Against your {ItemNaming.Describe(mine, Knowledge, withArticle: false, full: false)}:";
        return head + "\n" + string.Join("\n", lines.Select(l => $"{(l.Tone > 0 ? "+" : l.Tone < 0 ? "−" : " ")} {l.What}: {l.Yours} → {l.This}"));
    }

}
