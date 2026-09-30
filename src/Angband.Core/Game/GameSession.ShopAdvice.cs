using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>What a shop note says of an item for this character: its words, and whether it's better, worse or mixed.</summary>
/// <param name="Tone">1 better than what it would replace, -1 worse, 0 mixed or much the same.</param>
public sealed record ItemAdvice(string Text, int Tone);

// AVABand's shop notes ("will this suit me?"): for something you could wear or wield, how it compares
// with what it would replace — armour, damage per turn, a launcher's multiplier, speed and stats, the
// abilities and resistances it would bring or take away, and whether it would leave you burdened; for
// missiles, whether they fit your launcher. Only what AVABand's rules count is compared (blows come
// from your class and gear, not a weapon's weight; gloves don't hamper spells).
public sealed partial class GameSession
{
    private static readonly string[] AdviceModifiers =
    [
        ItemModifiers.Speed, ItemModifiers.Blows, ItemModifiers.Shots, ItemModifiers.Strength, ItemModifiers.Intelligence,
        ItemModifiers.Wisdom, ItemModifiers.Dexterity, ItemModifiers.Constitution, ItemModifiers.Stealth, ItemModifiers.Light,
    ];

    /// <summary>
    /// The note for <paramref name="item"/> (null for things that aren't worn, wielded or fired).
    /// <paramref name="known"/>: whether its properties are known (a shop's stock always is; for your
    /// own things, only once you know all its runes).
    /// </summary>
    public ItemAdvice? AdviceFor(Item item, bool known = true, bool buying = true)
    {
        if (item.IsAmmo) return AmmoAdvice(item);
        if (!item.IsWearable || item.Base.Slot == EquipSlot.None) return null;
        if (!known) return new ItemAdvice("not fully known yet", 0);

        var slot = Player.Inventory.SlotFor(item);
        var replaced = slot >= 0 ? Player.Inventory.Equipment[slot] : null;
        var good = new List<string>();
        var bad = new List<string>();
        void Delta(int value, string what, string format = "{0:+0;-0}")
        {
            if (value == 0) return;
            (value > 0 ? good : bad).Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, value) + " " + what);
        }

        switch (item.Base.Slot)
        {
            case EquipSlot.Weapon:
            {
                // Damage a turn: an average blow (dice and the weapon's own bonus) times the blows you'd have.
                double PerTurn(Item? w) =>
                    w is null ? 0 : (w.Damage.AverageTimesTwo / 2.0 + w.ToDam + Player.ToDam)
                                    * (Player.Blows + 100 * (w.Modifier(ItemModifiers.Blows) - (replaced?.Modifier(ItemModifiers.Blows) ?? 0))) / 100.0;
                var now = replaced is null ? 0 : (replaced.Damage.AverageTimesTwo / 2.0 + replaced.ToDam + Player.ToDam) * Player.Blows / 100.0;
                var then = PerTurn(item);
                var change = (int)Math.Round(then - now);
                if (change != 0) Delta(change, $"damage a turn (about {Math.Round(then)})");
                Delta(item.ToHit - (replaced?.ToHit ?? 0), "to hit");
                break;
            }
            case EquipSlot.Bow:
                if (item.Multiplier != (replaced?.Multiplier ?? 0))
                    (item.Multiplier > (replaced?.Multiplier ?? 0) ? good : bad).Add($"x{item.Multiplier} shots (now {(replaced is null ? "none" : $"x{replaced.Multiplier}")})");
                Delta(item.ToDam - (replaced?.ToDam ?? 0), "damage a shot");
                if (replaced is not null && replaced.Base.AmmoClass != item.Base.AmmoClass)
                    bad.Add($"fires {AmmoWord(item)}, not your {AmmoWord(replaced)}");
                break;
            default:
                // Armour's and jewellery's to-hit and to-dam count for every attack.
                Delta(item.ToHit - (replaced?.ToHit ?? 0), "to hit");
                Delta(item.ToDam - (replaced?.ToDam ?? 0), "damage");
                break;
        }
        Delta(item.Armour + item.ToAc - (replaced is null ? 0 : replaced.Armour + replaced.ToAc), "armour");
        foreach (var mod in AdviceModifiers.Where(m => m != ItemModifiers.Blows || item.Base.Slot != EquipSlot.Weapon))
            Delta(item.Modifier(mod) - (replaced?.Modifier(mod) ?? 0), ModifierWord(mod));

        // Abilities and resistances: what it would bring that nothing else gives, and what it would take away.
        var others = Player.Inventory.Equipped.Where(i => i != replaced).SelectMany(i => i.Resists).Concat(Player.IntrinsicResists.Keys).ToHashSet();
        foreach (var r in item.Resists.Where(r => !others.Contains(r) && !(replaced?.Resists.Contains(r) ?? false)).Order())
            good.Add(AbilityWord(r));
        if (replaced is not null)
            foreach (var r in replaced.Resists.Where(r => !others.Contains(r) && !item.Resists.Contains(r)).Order())
                bad.Add("loses " + AbilityWord(r));
        if (item.IsCursed) bad.Add("cursed");

        if (buying && Player.Inventory.TotalWeight + item.Weight > Player.WeightLimit / 2) bad.Add("too heavy: you'd be slowed");

        var against = replaced is null ? $"for your empty {SlotWord(item.Base.Slot)} slot" : $"vs your {ItemNaming.Describe(replaced, Knowledge, withArticle: false, full: false)}";
        var parts = good.Concat(bad).ToList();
        var text = parts.Count == 0 ? $"{against}: much the same" : $"{against}: {string.Join(", ", parts)}";
        return new ItemAdvice(text, good.Count > 0 && bad.Count == 0 ? 1 : bad.Count > 0 && good.Count == 0 ? -1 : 0);
    }

    private ItemAdvice AmmoAdvice(Item ammo)
    {
        if (ammo.IsThrowing && ammo.Base.AmmoClass is null) return new ItemAdvice("for throwing", 0);
        return Player.Inventory.Bow is { } bow
            ? bow.Base.AmmoClass == ammo.Base.AmmoClass
                ? new ItemAdvice($"fits your {ItemNaming.Describe(bow, Knowledge, withArticle: false, full: false)}", 1)
                : new ItemAdvice($"not for your {ItemNaming.Describe(bow, Knowledge, withArticle: false, full: false)}", -1)
            : new ItemAdvice("you have no launcher for these", -1);
    }

    private static string AmmoWord(Item launcher) => launcher.Base.AmmoClass is { Length: > 0 } ammo ? ammo + "s" : "missiles";

    private static string SlotWord(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon => "weapon", EquipSlot.Bow => "launcher", EquipSlot.Ring => "ring", EquipSlot.Amulet => "amulet",
        EquipSlot.Light => "light", EquipSlot.Body => "body armour", EquipSlot.Cloak => "cloak", EquipSlot.Shield => "shield",
        EquipSlot.Head => "helm", EquipSlot.Hands => "gloves", EquipSlot.Feet => "boots", _ => "equipment",
    };

    private static string ModifierWord(string mod) => mod switch
    {
        ItemModifiers.Speed => "speed", ItemModifiers.Blows => "blows", ItemModifiers.Shots => "shots (tenths)",
        ItemModifiers.Strength => "STR", ItemModifiers.Intelligence => "INT", ItemModifiers.Wisdom => "WIS",
        ItemModifiers.Dexterity => "DEX", ItemModifiers.Constitution => "CON", ItemModifiers.Stealth => "stealth",
        ItemModifiers.Light => "light", _ => mod,
    };

    /// <summary>A resistance or ability in plain words: "resist fire", "free action".</summary>
    private string AbilityWord(string id) => id switch
    {
        "free_act" => "free action",
        "see_invis" => "see invisible",
        "hold_life" => "hold life",
        "conf" => "protection from confusion",
        "blind" => "protection from blindness",
        "fear" => "protection from fear",
        "stun" => "protection from stunning",
        _ when id.StartsWith("sust_", StringComparison.Ordinal) => "sustain " + id[5..].ToUpperInvariant(),
        _ when id.StartsWith("im_", StringComparison.Ordinal) => "immunity to " + (Data.Element(id[3..])?.Name ?? id[3..]),
        _ => "resist " + (Data.Element(id)?.Name ?? id),
    };
}
