using System.Collections.ObjectModel;
using Angband.Core.Items;
using Angband.Core.Records;
using CommunityToolkit.Mvvm.ComponentModel;

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

/// <summary>The knowledge browser (Angband '~'): monsters, objects, runes, egos and artifacts.</summary>
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

    public MonsterKnowledgeViewModel Monsters { get; }
    public KnowledgeCategoryViewModel Objects { get; }
    public KnowledgeCategoryViewModel Runes { get; }
    public KnowledgeCategoryViewModel Egos { get; }
    public KnowledgeCategoryViewModel Artifacts { get; }

    /// <summary>The open tab (0 monsters, 1 objects, 2 runes, 3 egos, 4 artifacts).</summary>
    [ObservableProperty] private int _selectedTab;
}

public sealed partial class MainWindowViewModel
{
    /// <summary>Raised to show the knowledge browser.</summary>
    public event Action<KnowledgeViewModel>? KnowledgeRequested;

    public KnowledgeViewModel CreateKnowledge() =>
        new(CreateMonsterKnowledge(), CreateObjectKnowledge(), CreateRuneKnowledge(), CreateEgoKnowledge(), CreateArtifactKnowledge());

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
                var color = flavor is not null ? _cells.Color(flavor.Color) : BaseColor(o.Base);
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
                var b = e.Bases.FirstOrDefault() ?? "";
                return new KnowledgeRow(BaseGlyph(b), BaseColor(b), Capitalize(e.Name),
                    string.Join(", ", e.Bases.Select(BaseName).Distinct()),
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
