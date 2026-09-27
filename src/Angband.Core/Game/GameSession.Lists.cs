using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>
/// One line of the monster or object list: a heading, or an entry with its symbol, text, where it is
/// and the colour Angband gives the line. Colours are names from colors.json.
/// </summary>
public sealed record VisibleListRow(string Text, string Color, string Location = "", char? Glyph = null, string? GlyphColor = null,
    string? TileKey = null, bool IsHeading = false)
{
    public static VisibleListRow Heading(string text) => new(text, "White", IsHeading: true);
    public static readonly VisibleListRow Blank = new("", "White", IsHeading: true);
}

/// <summary>
/// The monster list ('[', Angband mon-list.c / ui-mon-list.c) and the object list (']', obj-list.c /
/// ui-obj-list.c): what the player can see, and what else they know is on the level.
/// </summary>
public sealed partial class GameSession
{
    private sealed class MonsterListEntry(MonsterRaceDef race)
    {
        public MonsterRaceDef Race { get; } = race;
        public readonly int[] Count = new int[2];
        public readonly int[] Asleep = new int[2];
        public readonly Loc[] Offset = new Loc[2];
    }

    /// <summary>
    /// Angband monster_list_collect and monster_list_format_textblock: the monsters in line of sight,
    /// then those only sensed ("You are aware of..."), one line per race — deepest first, or by the
    /// experience a kill would give. A lone monster's line says where it is ("3 N 4 E").
    /// </summary>
    public IReadOnlyList<VisibleListRow> MonsterList(bool byExperience = false)
    {
        var entries = new List<MonsterListEntry>();
        foreach (var m in Level.Monsters.All)
        {
            if (!(m.IsVisible || m.IsDetected) || m.Camouflaged) continue;
            var entry = entries.FirstOrDefault(e => e.Race == m.Race);
            if (entry is null) entries.Add(entry = new MonsterListEntry(m.Race));
            var section = ProjectionPath.Projectable(Level, Player.Position, m.Position, MaxRange) ? 0 : 1;
            entry.Count[section]++;
            if (m.IsAsleep) entry.Asleep[section]++;
            entry.Offset[section] = m.Position - Player.Position;
        }

        // Angband monster_list_standard_compare (deepest first) or monster_list_compare_exp.
        var level = Math.Max(1, Player.Level);
        var sorted = byExperience
            ? entries.OrderByDescending(e => (long)e.Race.Experience * e.Race.Depth / level).ToList()
            : entries.OrderByDescending(e => e.Race.Depth).ToList();

        var rows = new List<VisibleListRow>();
        var inView = sorted.Sum(e => e.Count[0]);
        MonsterSection(rows, sorted, 0, "You can see", others: false);
        if (sorted.Any(e => e.Count[1] > 0))
        {
            rows.Add(VisibleListRow.Blank);
            MonsterSection(rows, sorted, 1, "You are aware of", others: inView > 0);
        }
        return rows;
    }

    private void MonsterSection(List<VisibleListRow> rows, List<MonsterListEntry> entries, int section, string prefix, bool others)
    {
        var total = entries.Sum(e => e.Count[section]);
        if (total == 0)
        {
            rows.Add(VisibleListRow.Heading($"{prefix} no monsters."));
            return;
        }
        rows.Add(VisibleListRow.Heading($"{prefix} {total} {(others ? "other " : "")}monster{(total == 1 ? "" : "s")}:"));
        foreach (var e in entries.Where(e => e.Count[section] > 0))
        {
            var count = e.Count[section];
            var asleep = e.Asleep[section];
            var sleep = asleep > 0 && count > 1 ? $" ({asleep} asleep)" : asleep == 1 && count == 1 ? " (asleep)" : "";
            var location = count == 1 ? ListLocation(e.Offset[section]) : "";
            // Angband monster_list_entry_line_color: uniques violet, out-of-depth monsters red.
            var color = e.Race.IsUnique ? "Violet" : e.Race.Depth > Player.Depth ? "Red" : "White";
            rows.Add(new VisibleListRow(MonsterListName(e.Race, count) + sleep, color, location, e.Race.Glyph, e.Race.Color,
                "monster:" + e.Race.Id));
        }
    }

    /// <summary>Angband get_mon_name: "[U] Grip, Farmer Maggot's Dog", "  1 jackal", "  3 jackals".</summary>
    public static string MonsterListName(MonsterRaceDef race, int count)
    {
        var prefix = race.IsUnique ? "[U] " : $"{count,3} ";
        if (count == 1) return prefix + race.Name;
        return prefix + (race.Plural ?? race.Name + (race.Name.EndsWith('s') ? "es" : "s"));
    }

    /// <summary>"3 N 4 E": rows then columns from the player, as Angband's lists give them.</summary>
    private static string ListLocation(Loc offset) =>
        $"{Math.Abs(offset.Y)} {(offset.Y <= 0 ? "N" : "S")} {Math.Abs(offset.X)} {(offset.X <= 0 ? "W" : "E")}";

    private sealed record ObjectListEntry(Item Item, Loc Offset, int Section);

    /// <summary>
    /// Angband object_list_collect and object_list_format_textblock: every object the player knows
    /// of on the level — piles in view as they are, others as last seen — except gold and ignored
    /// things, in line of sight first. Artifacts lead, then unfamiliar kinds, then the rest by type,
    /// nearest first; each line says where it is.
    /// </summary>
    public IReadOnlyList<VisibleListRow> ObjectList()
    {
        var entries = new List<ObjectListEntry>();
        void Add(Loc p, Item item)
        {
            if (item.IsGold || IsIgnored(item)) return;
            var los = p == Player.Position || ProjectionPath.Projectable(Level, Player.Position, p, MaxRange);
            entries.Add(new ObjectListEntry(item, p - Player.Position, los ? 0 : 1));
        }
        foreach (var p in Level.AllLocs())
        {
            if (Level[p].Has(SquareFlags.Seen))
            {
                foreach (var item in Level.Objects.At(p)) Add(p, item);
                if (Level.Objects.At(p).Count == 0 && ObjectShownAt(p) is { } disguise) Add(p, disguise); // a mimic
            }
            else if (Known.RememberedObject(p) is { } remembered) Add(p, remembered);
        }

        var sorted = entries.Order(Comparer<ObjectListEntry>.Create(CompareListObjects)).ToList();
        var rows = new List<VisibleListRow>();
        ObjectSection(rows, sorted, 0, "You can see", others: false);
        if (sorted.Any(e => e.Section == 1))
        {
            rows.Add(VisibleListRow.Blank);
            ObjectSection(rows, sorted, 1, "You are aware of", others: sorted.Any(e => e.Section == 0));
        }
        return rows;
    }

    private void ObjectSection(List<VisibleListRow> rows, List<ObjectListEntry> entries, int section, string prefix, bool others)
    {
        var list = entries.Where(e => e.Section == section).ToList();
        if (list.Count == 0)
        {
            rows.Add(VisibleListRow.Heading($"{prefix} no objects."));
            return;
        }
        rows.Add(VisibleListRow.Heading($"{prefix} {list.Count} {(others ? "other " : "")}object{(list.Count == 1 ? "" : "s")}:"));
        foreach (var e in list)
        {
            var item = e.Item;
            var flavor = item.IsFlavored ? Knowledge.Flavor(item.Kind) : null;
            rows.Add(new VisibleListRow(ObjectListName(item), ObjectLineColor(item), ListLocation(e.Offset), item.Base.Glyph,
                flavor?.Color ?? item.Base.Color, "object:" + item.Kind.Id));
        }
    }

    /// <summary>Angband object_list_format_name: the article or count right-aligned in three columns ("  a", " 12", "the").</summary>
    private string ObjectListName(Item item)
    {
        var name = Describe(item);
        var space = name.IndexOf(' ');
        return space is > 0 and <= 3 ? $"{name[..space],3} {name[(space + 1)..]}" : "    " + name;
    }

    /// <summary>Angband object_list_entry_line_attribute: artifacts violet, unfamiliar kinds light red, worthless things slate.</summary>
    private string ObjectLineColor(Item item) =>
        item.IsArtifact && Knowledge.IsFullyKnown(item) ? "Violet"
        : item.IsFlavored && !Knowledge.IsAware(item.Kind) ? "LightRed"
        : item.Kind.Cost == 0 ? "Slate"
        : "White";

    /// <summary>Angband compare_items (known artifacts, then unfamiliar kinds, then worthless last, by type), then distance.</summary>
    private int CompareListObjects(ObjectListEntry a, ObjectListEntry b)
    {
        int Rank(Item i) =>
            i.IsArtifact && Knowledge.IsFullyKnown(i) ? 0
            : i.IsFlavored && !Knowledge.IsAware(i.Kind) ? 1
            : i.Kind.Cost != 0 ? 2 : 3;
        var c = Rank(a.Item).CompareTo(Rank(b.Item));
        if (c != 0) return c;
        c = BaseOrder(a.Item).CompareTo(BaseOrder(b.Item));
        if (c != 0) return c;
        c = KindOrder(a.Item).CompareTo(KindOrder(b.Item));
        if (c != 0) return c;
        static int Distance(Loc d) => d.X * d.X + d.Y * d.Y;
        return Distance(a.Offset).CompareTo(Distance(b.Offset));
    }

    private Dictionary<string, int>? _baseOrder, _kindOrder;

    // Angband compare_types: by tval, then sval — here, the order of object_bases.json and objects.json.
    private int BaseOrder(Item item) =>
        (_baseOrder ??= Data.ObjectBases.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i)).GetValueOrDefault(item.Base.Id);
    private int KindOrder(Item item) =>
        (_kindOrder ??= Data.Objects.Select((k, i) => (k.Id, i)).ToDictionary(x => x.Id, x => x.i)).GetValueOrDefault(item.Kind.Id);
}
