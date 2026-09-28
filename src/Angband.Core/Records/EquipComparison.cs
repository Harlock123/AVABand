using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Core.Records;

/// <summary>One column of the comparison: a short heading, what it stands for, and its rune.</summary>
public sealed record EquipColumn(string Heading, string Meaning, string Rune, int Width);

/// <summary>One wearable item in the comparison: where it is, its slot and name, and a cell per column.</summary>
public sealed record EquipComparisonRow(string Source, EquipSlot Slot, string Name, string Combat, IReadOnlyList<string> Cells, Item Item);

/// <summary>
/// Angband 4.2's equippable comparison (ui-equip-cmp.c): every wearable item you have — worn, in
/// the pack, underfoot, at home, and (if asked) in the shops — side by side: its combat numbers,
/// then a cell for each modifier, resistance, protection, sustain and ability. As in 4.2 only
/// what you know is shown: a property whose rune you haven't learned reads "?" on an item not
/// yet fully known.
/// </summary>
public static class EquipComparison
{
    public static readonly IReadOnlyList<EquipColumn> Columns =
    [
        Mod("St", "strength", "str"), Mod("In", "intelligence", "int"), Mod("Wi", "wisdom", "wis"),
        Mod("Dx", "dexterity", "dex"), Mod("Cn", "constitution", "con"), Mod("Sl", "stealth", "stealth"),
        Mod("Sp", "speed", "speed"), Mod("Bl", "blows", "blows"), Mod("Sh", "shots", "shots"),
        Mod("Lt", "light", "light"), Mod("Iv", "infravision", "infra"), Mod("Tu", "tunnelling", "tunnel"),
        Res("a", "acid"), Res("e", "electricity", "elec"), Res("f", "fire"), Res("c", "cold"), Res("p", "poison", "pois"),
        Res("l", "light"), Res("d", "dark"), Res("s", "sound"), Res("h", "shards"), Res("x", "nexus"),
        Res("n", "nether"), Res("C", "chaos"), Res("D", "disenchantment", "disen"),
        Res("F", "free action", "free_act"), Res("I", "see invisible", "see_invis"), Res("f", "protection from fear", "fear"),
        Res("b", "protection from blindness", "blind"), Res("c", "protection from confusion", "conf"),
        Res("S", "sustained strength", "sust_str"), Res("I", "sustained intelligence", "sust_int"),
        Res("W", "sustained wisdom", "sust_wis"), Res("D", "sustained dexterity", "sust_dex"),
        Res("C", "sustained constitution", "sust_con"),
        Flag("H", "hold life", ItemFlags.HoldLife), Flag("R", "regeneration", ItemFlags.Regen),
        Flag("s", "slow digestion", ItemFlags.SlowDigest), Flag("T", "telepathy", ItemFlags.Telepathy),
    ];

    /// <summary>Where the columns fall into groups, for the heading line: (first column, name).</summary>
    public static readonly IReadOnlyList<(int Start, string Name)> Groups =
        [(0, "modifiers"), (12, "resistances"), (25, "prot"), (30, "sust"), (35, "abil")];

    private static EquipColumn Mod(string h, string meaning, string mod) => new(h, meaning, RuneIds.Modifier(mod), 3);
    private static EquipColumn Res(string h, string meaning, string? id = null) => new(h, meaning, RuneIds.Resist(id ?? meaning), 1);
    private static EquipColumn Flag(string h, string meaning, string flag) => new(h, meaning, RuneIds.Flag(flag), 1);

    /// <summary>The rows: worn first, then the pack, the floor, home and (optionally) the shops; filtered by slot.</summary>
    public static IReadOnlyList<EquipComparisonRow> Rows(GameSession game, bool includeShops = false, EquipSlot? slot = null)
    {
        var sources = new List<(string Source, IEnumerable<Item> Items)>
        {
            ("worn", game.Player.Inventory.Equipped),
            ("pack", game.Player.Inventory.Pack),
            ("floor", game.Level.Objects.At(game.Player.Position)),
        };
        foreach (var store in game.Stores.Values.Where(s => s.IsHome))
            sources.Add(("home", store.Stock));
        if (includeShops)
            foreach (var store in game.Stores.Values.Where(s => !s.IsHome))
                sources.Add((store.Name.Split(' ')[0], store.Stock)); // "General", "Armoury", "Weapon", "Alchemy"...

        return [.. sources
            .SelectMany(s => s.Items.Where(i => i.Base.IsWearable && !i.IsAmmo && (slot is null || i.Base.Slot == slot))
                .Select(i => Row(game, s.Source, i)))];
    }

    private static EquipComparisonRow Row(GameSession game, string source, Item item)
    {
        var k = game.Knowledge;
        var fully = k.IsFullyKnown(item);
        var cells = Columns.Select(c => Cell(k, item, c, fully)).ToList();
        // The name without the numbers (they have their own column).
        var name = ItemNaming.Describe(item, k, withArticle: false, full: false);
        return new EquipComparisonRow(source, item.Base.Slot, name, Combat(k, item), cells, item);
    }

    private static string Cell(PlayerKnowledge k, Item item, EquipColumn column, bool fully)
    {
        var has = item.Runes().Contains(column.Rune);
        if (!k.KnowsRune(column.Rune)) return !fully ? "?" : ".";
        if (!has) return ".";
        if (column.Width == 1) return column.Heading;
        var value = item.Modifier(column.Rune[4..]);
        return value.ToString("+0;-0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>"2d5 (+3,+4)", "x3 (+5,+0)", "[8,+2]": the numbers the item's name shows, as far as known.</summary>
    private static string Combat(PlayerKnowledge k, Item item)
    {
        string Plus(int v, string rune) => k.KnowsRune(rune) ? v.ToString("+0;-0", System.Globalization.CultureInfo.InvariantCulture) : "?";
        if (item.Base.IsWeapon) return $"{item.Damage} ({Plus(item.ToHit, RuneIds.ToHit)},{Plus(item.ToDam, RuneIds.ToDam)})";
        if (item.Base.Slot == EquipSlot.Bow) return $"x{item.Multiplier} ({Plus(item.ToHit, RuneIds.ToHit)},{Plus(item.ToDam, RuneIds.ToDam)})";
        if (item.Armour > 0 || item.ToAc != 0) return $"[{item.Armour},{Plus(item.ToAc, RuneIds.ToAc)}]";
        return "";
    }
}
