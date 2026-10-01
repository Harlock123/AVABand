using System.Collections.ObjectModel;
using Angband.Core.Items;
using Angband.Core.Records;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>One line of an object knowledge list: a glyph, a name, a note, and the text shown when chosen.</summary>
public sealed record KnowledgeRow(string Glyph, uint GlyphColor, string Name, string Note, Func<string> Describe,
    Angband.Core.Definitions.ObjectKindDef? Kind = null, Angband.Core.Definitions.EgoItemDef? Ego = null)
{
    public global::Avalonia.Media.IBrush GlyphBrush { get; } =
        new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(global::Avalonia.Media.Color.FromUInt32(GlyphColor));
}

/// <summary>One object knowledge tab (objects, runes, egos or artifacts): pick a line to read about it.</summary>
public sealed partial class KnowledgeCategoryViewModel : ObservableObject
{
    private readonly Func<Angband.Core.Definitions.ObjectKindDef, string?>? _getNote;
    private readonly Action<Angband.Core.Definitions.ObjectKindDef, string?>? _setNote;

    /// <param name="getNote">For object kinds: reads the auto-inscription (null: none can be set here).</param>
    /// <param name="setNote">For object kinds: sets the auto-inscription.</param>
    public KnowledgeCategoryViewModel(string title, string summary, string emptyText, IEnumerable<KnowledgeRow> rows,
        Func<Angband.Core.Definitions.ObjectKindDef, string?>? getNote = null, Action<Angband.Core.Definitions.ObjectKindDef, string?>? setNote = null)
    {
        _getNote = getNote;
        _setNote = setNote;
        Title = title;
        Summary = summary;
        EmptyText = emptyText;
        foreach (var row in rows) Rows.Add(row);
        Selected = Rows.FirstOrDefault();
    }

    public string Title { get; }
    public string Summary { get; }
    public string EmptyText { get; }
    public ObservableCollection<KnowledgeRow> Rows { get; } = [];
    public bool IsEmpty => Rows.Count == 0;

    [ObservableProperty] private KnowledgeRow? _selected;
    [ObservableProperty] private string _text = "";

    /// <summary>The selected kind's auto-inscription (Angband's knowledge menu "Inscribe").</summary>
    [ObservableProperty] private string _autoInscription = "";

    public bool CanAutoInscribe => _setNote is not null && Selected?.Kind is not null;

    partial void OnSelectedChanged(KnowledgeRow? value)
    {
        Text = value?.Describe() ?? "";
        AutoInscription = value?.Kind is { } kind && _getNote is not null ? _getNote(kind) ?? "" : "";
        OnPropertyChanged(nameof(CanAutoInscribe));
        OnPropertyChanged(nameof(CanIgnore));
        OnPropertyChanged(nameof(IsIgnored));
    }

    /// <summary>Whether the selected line can be ignored (kinds of ignorable objects, and egos).</summary>
    public bool CanIgnore => Selected is { } row && _canIgnore?.Invoke(row) == true;

    /// <summary>Whether the selected kind or ego is ignored (Angband's knowledge menu 's' / ego menu).</summary>
    public bool IsIgnored
    {
        get => Selected is { } row && _isIgnored?.Invoke(row) == true;
        set
        {
            if (Selected is not { } row || _setIgnored is null || value == IsIgnored) return;
            _setIgnored(row, value);
            OnPropertyChanged();
            Text = row.Describe();
        }
    }

    private Func<KnowledgeRow, bool>? _canIgnore;
    private Func<KnowledgeRow, bool>? _isIgnored;
    private Action<KnowledgeRow, bool>? _setIgnored;

    /// <summary>Lets lines be ignored from the browser.</summary>
    public KnowledgeCategoryViewModel WithIgnoring(Func<KnowledgeRow, bool> canIgnore, Func<KnowledgeRow, bool> isIgnored, Action<KnowledgeRow, bool> setIgnored)
    {
        (_canIgnore, _isIgnored, _setIgnored) = (canIgnore, isIgnored, setIgnored);
        OnPropertyChanged(nameof(CanIgnore));
        OnPropertyChanged(nameof(IsIgnored));
        return this;
    }

    /// <summary>Gives every object of the selected kind this inscription as it is found (empty clears it).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void SetAutoInscription()
    {
        if (Selected?.Kind is not { } kind || _setNote is null) return;
        _setNote(kind, string.IsNullOrWhiteSpace(AutoInscription) ? null : AutoInscription.Trim());
        Text = Selected.Describe();
    }
}

/// <summary>One line of the equipment comparison, as shown (a monospaced row) with its item.</summary>
public sealed record EquipLine(string Text, Item Item);

/// <summary>
/// Angband 4.2's equippable comparison: every wearable item you have, side by side, filtered by
/// slot, with or without the shops' goods. Picking a line describes the item.
/// </summary>
public sealed partial class EquipComparisonViewModel : ObservableObject
{
    private readonly Angband.Core.Game.GameSession _game;
    private readonly Func<Item, string> _describe;

    public EquipComparisonViewModel(Angband.Core.Game.GameSession game, Func<Item, string> describe)
    {
        _game = game;
        _describe = describe;
        Slots = ["All", .. Enum.GetValues<Angband.Core.Definitions.EquipSlot>()
            .Where(s => s != Angband.Core.Definitions.EquipSlot.None).Select(s => s.ToString())];
        Fill();
    }

    public IReadOnlyList<string> Slots { get; }
    public ObservableCollection<EquipLine> Lines { get; } = [];
    public string Groups { get; } = GroupLine();
    public string Headings { get; } = HeadingLine();
    public string Legend { get; } = string.Join(";  ", EquipComparison.Columns.Select(c => $"{c.Heading} {c.Meaning}").Distinct());

    [ObservableProperty] private string _slot = "All";
    [ObservableProperty] private bool _includeShops;
    [ObservableProperty] private EquipLine? _selected;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _summary = "";

    partial void OnSlotChanged(string value) => Fill();
    partial void OnIncludeShopsChanged(bool value) => Fill();
    partial void OnSelectedChanged(EquipLine? value) => Text = value is null ? "" : _describe(value.Item);

    private void Fill()
    {
        var slot = Enum.TryParse<Angband.Core.Definitions.EquipSlot>(Slot, out var s) ? s : (Angband.Core.Definitions.EquipSlot?)null;
        var rows = EquipComparison.Rows(_game, IncludeShops, slot);
        Lines.Clear();
        foreach (var r in rows) Lines.Add(new EquipLine(Line(r), r.Item));
        Summary = rows.Count == 1 ? "1 item" : $"{rows.Count} items";
        Selected = Lines.FirstOrDefault();
    }

    private const int SourceWidth = 8, NameWidth = 30, CombatWidth = 14;

    private static string Cells(Func<EquipColumn, int, string> cell)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < EquipComparison.Columns.Count; i++)
        {
            if (EquipComparison.Groups.Any(g => g.Start == i && i > 0)) sb.Append(' ');
            var c = EquipComparison.Columns[i];
            sb.Append(cell(c, i).PadLeft(c.Width));
        }
        return sb.ToString();
    }

    private static string Fit(string s, int width) => s.Length > width ? s[..(width - 1)] + "~" : s.PadRight(width);

    public static string Line(EquipComparisonRow r) =>
        $"{Fit(r.Source, SourceWidth)} {Fit(r.Name, NameWidth)} {Fit(r.Combat, CombatWidth)} {Cells((_, i) => r.Cells[i])}";

    private static string HeadingLine() => $"{"Where",-SourceWidth} {"Item",-NameWidth} {"",-CombatWidth} {Cells((c, _) => c.Heading)}";

    private static string GroupLine()
    {
        var pad = new string(' ', SourceWidth + NameWidth + CombatWidth + 3);
        var sb = new System.Text.StringBuilder(pad);
        var cells = Cells((c, _) => new string(' ', c.Width));
        var at = new List<int>();
        for (int i = 0, x = 0; i < EquipComparison.Columns.Count; i++)
        {
            if (EquipComparison.Groups.Any(g => g.Start == i && i > 0)) x++;
            at.Add(x);
            x += EquipComparison.Columns[i].Width;
        }
        var line = new char[cells.Length + 12];
        Array.Fill(line, ' ');
        foreach (var (start, name) in EquipComparison.Groups)
            for (var j = 0; j < name.Length && at[start] + j < line.Length; j++) line[at[start] + j] = name[j];
        return (sb.Append(line).ToString()).TrimEnd();
    }
}

/// <summary>
/// The knowledge browser (Angband '~'): monsters, objects, runes, egos, artifacts, terrain features,
/// traps, the contents of your home and your history.
/// </summary>
public sealed partial class KnowledgeViewModel : ObservableObject
{
    public KnowledgeViewModel(MonsterKnowledgeViewModel monsters, KnowledgeCategoryViewModel objects,
        KnowledgeCategoryViewModel runes, KnowledgeCategoryViewModel egos, KnowledgeCategoryViewModel artifacts)
    {
        Monsters = monsters;
        Objects = objects;
        Runes = runes;
        Egos = egos;
        Artifacts = artifacts;
    }

    /// <summary>The curses you know of (AVABand's own page; 4.2.5 lists them among the runes).</summary>
    public KnowledgeCategoryViewModel? Curses { get; init; }

    public MonsterKnowledgeViewModel Monsters { get; }
    public KnowledgeCategoryViewModel Objects { get; }
    public KnowledgeCategoryViewModel Runes { get; }
    public KnowledgeCategoryViewModel Egos { get; }
    public KnowledgeCategoryViewModel Artifacts { get; }
    public KnowledgeCategoryViewModel? Features { get; init; }
    public KnowledgeCategoryViewModel? Traps { get; init; }
    public KnowledgeCategoryViewModel? Home { get; init; }
    public KnowledgeCategoryViewModel? Shapes { get; init; }
    public EquipComparisonViewModel? Equipment { get; init; }
    /// <summary>AVABand's quest journal (the last tab).</summary>
    public KnowledgeCategoryViewModel? Quests { get; init; }
    public KnowledgeCategoryViewModel? Feats { get; init; }

    /// <summary>AVABand: what you carry or keep at home that the shops' note calls better than what you have on.</summary>
    public KnowledgeCategoryViewModel? Upgrades { get; init; }

    /// <summary>The character history, as Angband's history screen and dump show it (the lines the filter lets through).</summary>
    [ObservableProperty] private string _history = "";

    /// <summary>The history's lines, read afresh each time the page is filtered.</summary>
    public Func<IReadOnlyList<Angband.Core.Game.HistoryEntry>>? HistoryEntries { get; init; }

    /// <summary>What the History page can show: everything, or one kind of line.</summary>
    public static IReadOnlyList<string> HistoryFilters { get; } = ["Everything", "Your notes", "Levels reached", "Uniques killed", "Artifacts"];

    /// <summary>The kind of line shown (an index into <see cref="HistoryFilters"/>).</summary>
    [ObservableProperty] private int _historyFilter;

    /// <summary>Words the shown lines must contain (any case; empty: all).</summary>
    [ObservableProperty] private string _historySearch = "";

    /// <summary>How many lines are shown, of how many.</summary>
    [ObservableProperty] private string _historySummary = "";

    partial void OnHistoryFilterChanged(int value) => RefreshHistory();
    partial void OnHistorySearchChanged(string value) => RefreshHistory();

    /// <summary>Whether a line is of the kind chosen: notes ("-- "), levels, uniques killed, artifacts found or missed.</summary>
    public static bool IsKind(Angband.Core.Game.HistoryEntry h, int filter) => filter switch
    {
        1 => h.Text.StartsWith("-- ", StringComparison.Ordinal),
        2 => h.Text.StartsWith("Reached level ", StringComparison.Ordinal),
        3 => h.Text.StartsWith("Killed ", StringComparison.Ordinal),
        4 => h.Artifact is not null,
        _ => true,
    };

    /// <summary>Angband do_cmd_knowledge_history: the turn, depth and note of each line.</summary>
    public static string FormatHistory(IEnumerable<Angband.Core.Game.HistoryEntry> lines)
    {
        var sb = new System.Text.StringBuilder("      Turn   Depth  Note\n");
        foreach (var h in lines) sb.Append($"{h.Turn,10}{h.Depth * 50,7}'  {h.Shown}\n");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Shows the lines the filter and the search let through.</summary>
    public void RefreshHistory()
    {
        if (HistoryEntries is null) return;
        var all = HistoryEntries();
        var words = HistorySearch.Trim();
        var shown = all.Where(h => IsKind(h, HistoryFilter)
                                   && (words.Length == 0 || h.Shown.Contains(words, StringComparison.OrdinalIgnoreCase))).ToList();
        History = FormatHistory(shown);
        HistorySummary = shown.Count == all.Count ? $"{all.Count} {(all.Count == 1 ? "line" : "lines")}" : $"{shown.Count} of {all.Count} lines";
    }

    /// <summary>A note being written on the History page.</summary>
    [ObservableProperty] private string _noteText = "";

    /// <summary>Writes a note in the history (as ':' does); false if it wasn't kept.</summary>
    public Func<string, bool>? WriteNote { get; init; }

    /// <summary>The History page's "Add note": the note goes in, the page shows it, the box empties.</summary>
    [RelayCommand]
    private void AddNote()
    {
        if (WriteNote?.Invoke(NoteText) != true) return;
        NoteText = "";
        RefreshHistory();
    }

    /// <summary>The open tab (0 monsters, 1 objects, 2 runes, 3 curses, 4 egos, 5 artifacts, 6 features, 7 traps, 8 shapes, 9 equipment, 10 home, 11 history, 12 quests, 13 feats, 14 upgrades).</summary>
    [ObservableProperty] private int _selectedTab;
}

public sealed partial class MainWindowViewModel
{
    /// <summary>Raised to show the knowledge browser.</summary>
    public event Action<KnowledgeViewModel>? KnowledgeRequested;

    public KnowledgeViewModel CreateKnowledge() => CreateKnowledge(CreateMonsterKnowledge());

    private KnowledgeViewModel CreateKnowledge(MonsterKnowledgeViewModel monsters)
    {
        var knowledge = new KnowledgeViewModel(monsters, CreateObjectKnowledge(), CreateRuneKnowledge(), CreateEgoKnowledge(), CreateArtifactKnowledge())
        {
            Curses = CreateCurseKnowledge(),
            Features = CreateFeatureKnowledge(),
            Traps = CreateTrapKnowledge(),
            Home = CreateHomeKnowledge(),
            Shapes = CreateShapeKnowledge(),
            Equipment = new EquipComparisonViewModel(_game, Inspect),
            HistoryEntries = () => _game.History,
            WriteNote = text =>
            {
                if (!_game.AddNote(text)) return false;
                Refresh();
                return true;
            },
            Quests = CreateQuestJournal(),
            Feats = CreateFeatsPage(),
            Upgrades = CreateUpgrades(),
        };
        knowledge.RefreshHistory();
        return knowledge;
    }

    /// <summary>
    /// AVABand's Upgrades page: everything you carry or keep at home that the shops' note ("will this
    /// suit me?") calls better than what you have on — or mixed, better in some ways — best first, with
    /// the note and the item's description. Only what you know of them counts; bags you carry already
    /// count, so only those at home are weighed.
    /// </summary>
    public KnowledgeCategoryViewModel CreateUpgrades()
    {
        var home = _game.Stores.Values.FirstOrDefault(s => s.IsHome)?.Stock ?? [];
        var rows = _game.Player.Inventory.Pack.Select(i => (Item: i, AtHome: false))
            .Concat(home.Select(i => (Item: i, AtHome: true)))
            .Where(x => !x.Item.IsAmmo && (x.AtHome || x.Item.Kind.CarryPercent <= 0 && x.Item.Kind.PackSlots <= 0))
            .Select(x => (x.Item, x.AtHome, Advice: _game.AdviceFor(x.Item, _game.Knowledge.IsFullyKnown(x.Item), buying: x.AtHome)))
            .Where(x => x.Advice is { Tone: > 0 } || x.Advice is { Tone: 0 } a && a.Text.StartsWith("Mixed", StringComparison.Ordinal))
            .OrderByDescending(x => x.Advice!.Tone).ThenBy(x => x.AtHome)
            .Select(x => new KnowledgeRow(x.Item.Base.Glyph.ToString(),
                _cells.Color(_game.Knowledge.Flavor(x.Item.Kind)?.Color ?? x.Item.Kind.Color ?? x.Item.Base.Color),
                Capitalize(_game.Describe(x.Item)), $"{(x.Advice!.Tone > 0 ? "better" : "mixed")}{(x.AtHome ? ", at home" : "")}",
                () => x.Advice.Text + "\n\n" + Inspect(x.Item)))
            .ToList();
        return new KnowledgeCategoryViewModel("Upgrades",
            rows.Count == 1 ? "1 thing you have would suit you better" : $"{rows.Count} things you have would suit you better (or in part)",
            "Nothing you carry or keep at home would suit you better than what you have on.", rows);
    }

    /// <summary>AVABand's quest journal: every quest taken or found, and the notice-board jobs taken.</summary>
    public KnowledgeCategoryViewModel CreateQuestJournal()
    {
        var rows = _game.QuestJournal()
            .Select(q => new KnowledgeRow(q.Done ? "+" : "!", _cells.Color(q.Done ? "Slate" : "Yellow"), q.Name, q.Done ? "done" : "under way",
                () => $"{q.Name}{(q.Done ? " (done)" : "")}\n\n{q.Text}"))
            .ToList();
        var active = rows.Count(r => r.Note == "under way");
        return new KnowledgeCategoryViewModel("Quests", rows.Count == 0 ? "No quests yet" : $"{active} under way, {rows.Count - active} done",
            _game.AvaQuestsOn ? "No quests yet. The Prancing Pony (9) in town has work, and there are things to be found below."
                : "This character was made without AVABand's quests.", rows);
    }

    /// <summary>
    /// Angband do_cmd_knowledge_features: every terrain feature (not the ones that only pretend to be
    /// another, as secret doors do), grouped as its knowledge menu groups them, with its description.
    /// </summary>
    public KnowledgeCategoryViewModel CreateFeatureKnowledge()
    {
        var rows = _data.Terrain.All
            .Where(t => t.Mimic is null && t.Name.Length > 0 && t.Id != "none")
            .Select(t => (Def: t, Group: FeatureGroup(t)))
            .OrderBy(x => x.Group.Order).ThenBy(x => x.Def.Index)
            .Select(x => new KnowledgeRow(x.Def.Glyph.ToString(), _cells.Color(x.Def.Color), Capitalize(x.Def.Name), x.Group.Name,
                () => $"{Capitalize(x.Def.Name)}\n\n{x.Def.Description}".TrimEnd()))
            .ToList();
        return new KnowledgeCategoryViewModel("Features", $"{rows.Count} terrain features", "", rows);
    }

    /// <summary>Angband's feature groups (fkind): floors, doors, stairs, walls, streamers, obstructions, stores, other.</summary>
    private static (int Order, string Name) FeatureGroup(Angband.Core.Definitions.TerrainDef t) =>
        t.Has(Angband.Core.Definitions.TerrainFlags.Shop) ? (6, "store")
        : t.Has(Angband.Core.Definitions.TerrainFlags.Stair) ? (2, "stairs")
        : t.Has(Angband.Core.Definitions.TerrainFlags.DoorAny) ? (1, "door")
        : t.HasAny(Angband.Core.Definitions.TerrainFlags.Magma | Angband.Core.Definitions.TerrainFlags.Quartz) ? (4, "mineral vein")
        : t.Has(Angband.Core.Definitions.TerrainFlags.Rubble) ? (5, "obstruction")
        : t.Has(Angband.Core.Definitions.TerrainFlags.Wall) ? (3, "wall")
        : t.Has(Angband.Core.Definitions.TerrainFlags.Floor) ? (0, "floor")
        : (7, "other");

    /// <summary>Angband do_cmd_knowledge_traps: every kind of trap, rune and web, with its description.</summary>
    public KnowledgeCategoryViewModel CreateTrapKnowledge()
    {
        var rows = _data.Traps
            .OrderBy(t => t.Warding || t.Web ? 1 : 0).ThenBy(t => t.MinDepth).ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new KnowledgeRow(t.Glyph.ToString(), _cells.Color(t.Color), Capitalize(t.Name),
                t.Warding ? "rune" : t.Web ? "web" : t.IsRune ? "magic rune" : "trap",
                () => $"{Capitalize(t.Name)}\n\n{t.Description}".TrimEnd()))
            .ToList();
        return new KnowledgeCategoryViewModel("Traps", $"{rows.Count} kinds of trap", "", rows);
    }

    /// <summary>Angband do_cmd_knowledge_shapechange: every shape (not "normal"), by name, with what it does.</summary>
    public KnowledgeCategoryViewModel CreateShapeKnowledge()
    {
        var rows = _data.Shapes.Where(s => s.Id != "normal").OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new KnowledgeRow("@", _cells.Color("White"), Capitalize(s.Name), s.Blows.Count > 0 ? "shape" : "",
                () => ObjectInfo.DescribeShape(_data, s)))
            .ToList();
        return new KnowledgeCategoryViewModel("Shapes", $"{rows.Count} shapes", "", rows);
    }

    /// <summary>Angband's "Display contents of home": what you keep there, readable from anywhere.</summary>
    public KnowledgeCategoryViewModel CreateHomeKnowledge()
    {
        var home = _game.Stores.Values.FirstOrDefault(s => s.IsHome);
        var rows = (home?.Stock ?? []).Select(i => new KnowledgeRow(i.Base.Glyph.ToString(), _cells.Color(_game.Knowledge.Flavor(i.Kind)?.Color ?? i.Kind.Color ?? i.Base.Color),
                Capitalize(_game.Describe(i)), i.Number > 1 ? $"x{i.Number}" : "", () => Inspect(i)))
            .ToList();
        return new KnowledgeCategoryViewModel("Home", rows.Count == 1 ? "1 thing at home" : $"{rows.Count} things at home",
            "Your home is empty.", rows);
    }

    /// <summary>Angband do_cmd_knowledge_history: the turn, depth and note of each line.</summary>
    public string HistoryText() => KnowledgeViewModel.FormatHistory(_game.History);

    private uint BaseColor(string baseId) => _cells.Color(_data.ObjectBase(baseId)?.Color ?? "White");
    private string BaseName(string baseId) => _data.ObjectBase(baseId) is { } b ? ItemNaming.Plain(b.Name, false) : baseId;
    private string BaseGlyph(string baseId) => (_data.ObjectBase(baseId)?.Glyph ?? '?').ToString();

    public KnowledgeCategoryViewModel CreateObjectKnowledge()
    {
        var k = _game.Knowledge;
        var order = _data.ObjectBases.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var rows = _data.Objects
            .Where(o => k.SeenKinds.Contains(o.Id) && o.Base != "gold")
            .OrderBy(o => order.GetValueOrDefault(o.Base, int.MaxValue)).ThenBy(o => o.Level).ThenBy(o => o.Name, StringComparer.Ordinal)
            .Select(o =>
            {
                var flavor = k.Flavor(o);
                var color = flavor is not null ? _cells.Color(flavor.Color) : o.Color is { } own ? _cells.Color(own) : BaseColor(o.Base);
                return new KnowledgeRow(BaseGlyph(o.Base), color, ObjectInfo.KindName(_game, o),
                    BaseName(o.Base), () => ObjectInfo.DescribeKind(_game, o), o);
            })
            .ToList();
        return new KnowledgeCategoryViewModel("Objects",
            rows.Count == 1 ? "1 kind of object seen" : $"{rows.Count} kinds of object seen",
            "You have not seen any objects yet.", rows, _game.Knowledge.KindNote, (kind, note) =>
            {
                _game.SetAutoInscription(kind, note);
                RefreshInventory();
            }).WithIgnoring(
                row => row.Kind is { } k && Ignoring.KindBases.Contains(k.Base),
                row => row.Kind is { } k && _game.IsKindIgnored(k),
                (row, ignored) =>
                {
                    _game.SetKindIgnored(row.Kind!, ignored);
                    RefreshInventory();
                    Refresh();
                });
    }

    public KnowledgeCategoryViewModel CreateRuneKnowledge()
    {
        var all = ObjectInfo.AllRunes(_data);
        var known = all.Where(_game.Knowledge.KnowsRune).ToList();
        var rows = known
            .Select(r => new KnowledgeRow("*", 0xFFFFD700, Capitalize(_game.RuneName(r)), RuneCategory(r), () => ObjectInfo.DescribeRune(_game, r)))
            .OrderBy(r => r.Note, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
        return new KnowledgeCategoryViewModel("Runes", $"{known.Count} of {all.Count} runes learned",
            "You have not learned any runes yet.", rows);
    }

    /// <summary>The curses whose runes you know: what each does, where it is found, what you carry it on.</summary>
    public KnowledgeCategoryViewModel CreateCurseKnowledge()
    {
        var rows = _data.Curses
            .Where(c => _game.Knowledge.KnowsRune(Angband.Core.Definitions.RuneIds.Curse(c.Id)))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c =>
            {
                var carried = _game.Player.Inventory.Equipped.Concat(_game.Player.Inventory.Pack).Any(i => i.Curses.Contains(c.Id));
                return new KnowledgeRow("*", 0xFFFF4040, Capitalize(c.Name), carried ? "carried" : "",
                    () => ObjectInfo.DescribeCurse(_game, c));
            })
            .ToList();
        return new KnowledgeCategoryViewModel("Curses", $"{rows.Count} of {_data.Curses.Count} curses known",
            "You have not learned of any curses yet.", rows);
    }

    private static string RuneCategory(string rune) => (rune.IndexOf(':') is var i and > 0 ? rune[..i] : "combat") switch
    {
        "mod" => "modifier", "slay" => "slay", "brand" => "brand", "resist" => "protection", "flag" => "ability",
        "curse" => "curse", _ => "combat",
    };

    public KnowledgeCategoryViewModel CreateEgoKnowledge()
    {
        var rows = _data.Egos
            .Where(e => _game.Knowledge.SeenEgos.Contains(e.Id))
            .OrderBy(e => e.Level).ThenBy(e => e.Name, StringComparer.Ordinal)
            .Select(e =>
            {
                var bases = e.AllBases(_data).ToList();
                var b = bases.FirstOrDefault() ?? "";
                return new KnowledgeRow(BaseGlyph(b), BaseColor(b), Capitalize(e.Name),
                    string.Join(", ", bases.Select(BaseName).Distinct()),
                    () => ObjectInfo.DescribeEgo(_game, e), Ego: e);
            })
            .ToList();
        return new KnowledgeCategoryViewModel("Egos", rows.Count == 1 ? "1 ego item identified" : $"{rows.Count} ego items identified",
            "You have not identified any ego items yet.", rows).WithIgnoring(
            row => row.Ego is not null,
            row => row.Ego is { } e && _game.IsEgoIgnored(e),
            (row, ignored) =>
            {
                _game.SetEgoIgnored(row.Ego!, ignored);
                RefreshInventory();
                Refresh();
            });
    }

    public KnowledgeCategoryViewModel CreateArtifactKnowledge()
    {
        var rows = _game.Artifacts
            .Where(a => _game.Knowledge.SeenArtifacts.Contains(a.Id))
            .OrderBy(a => a.Level).ThenBy(a => a.Name, StringComparer.Ordinal)
            .Select(a =>
            {
                var kind = _data.Object(a.Kind);
                var b = kind?.Base ?? "";
                return new KnowledgeRow(BaseGlyph(b), BaseColor(b), $"{(kind is null ? "" : Capitalize(ItemNaming.Plain(kind.Name, false)))} {a.Name}".Trim(), $"level {a.Level}",
                    () => ObjectInfo.DescribeArtifact(_game, a));
            })
            .ToList();
        return new KnowledgeCategoryViewModel("Artifacts", rows.Count == 1 ? "1 artifact found" : $"{rows.Count} artifacts found",
            "You have not found any artifacts yet.", rows);
    }
}
