using System.Collections.ObjectModel;
using Angband.Avalonia.Controls;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>A class that can learn a spell, and what it costs that class (yours marked).</summary>
public sealed record GrimoireClassLine(string Text, bool IsYours)
{
    public string Weight => IsYours ? "SemiBold" : "Normal";
}

/// <summary>A spell's entry in the Grimoire: its picture, what it does, and who can learn it.</summary>
public sealed record GrimoireSpell(string Name, Bitmap? Icon, string Description, IReadOnlyList<GrimoireClassLine> Classes,
    string YourNote, string BookName, string Accent)
{
    public bool HasYourNote => YourNote.Length > 0;
}

/// <summary>A spellbook's pages: its cover, where it's found, who reads it, and its spells in order.</summary>
public sealed record GrimoireBook(string Id, string Name, string Kind, Bitmap? Cover, string Where, string Readers,
    IReadOnlyList<GrimoireSpell> Spells, string Accent, string DropCap, string Intro, bool ForYou)
{
    public string Count => Spells.Count == 1 ? "1 spell" : $"{Spells.Count} spells";
    public double ListOpacity => ForYou ? 1 : 0.75;
}

/// <summary>A realm of magic: its sigil, colour, a word about it, and its books (town books first).</summary>
public sealed partial class GrimoireRealm : ObservableObject
{
    public GrimoireRealm(string id, string name, Bitmap? sigil, string accent, IReadOnlyList<GrimoireBook> books)
    {
        Id = id;
        Name = name;
        Sigil = sigil;
        Accent = accent;
        Books = books;
        _selectedBook = books.FirstOrDefault();
    }

    public string Id { get; }
    public string Name { get; }
    public Bitmap? Sigil { get; }
    public string Accent { get; }
    public IReadOnlyList<GrimoireBook> Books { get; }
    [ObservableProperty] private GrimoireBook? _selectedBook;
}

/// <summary>
/// The Grimoire (Help → Grimoire; AVABand's own): every spell in every book, realm by realm, each with
/// its picture (art/grimoire, from Dungeon Crawl Stone Soup's CC0 tiles by tools/grimoire_art.py), what
/// it does, and the level, mana and failure chance for each class that can learn it — yours marked.
/// </summary>
public sealed partial class GrimoireViewModel : ObservableObject
{
    /// <summary>Each realm's colour on the page, and a word about it to open its pages.</summary>
    private static readonly Dictionary<string, (string Accent, string Kind, string Intro)> RealmStyle = new()
    {
        ["arcane"] = ("#2f4f9e", "Magic Book",
            "Arcane magic is learned, not given: words of power studied by candlelight and spoken with care. Mages "
            + "master it, rogues borrow from it, and its books are full of bolts and balls of force, of ways to see "
            + "what is hidden, and of ways to be somewhere else when the danger comes."),
        ["divine"] = ("#8a6a12", "Holy Book",
            "Divine magic is asked for, and granted. Priests pray for it and paladins carry it into battle: light "
            + "against the dark, healing for the wounded, blessings for the faithful and ruin for evil things. "
            + "The Valar do not answer every prayer the same way twice."),
        ["nature"] = ("#2f6b2a", "Nature Book",
            "Nature magic is the old craft of the woods and the stone: druids sing it and rangers keep a little of it "
            + "by them. It calls the lightning, turns rock to mud and wounds to health, and lends the shapes of "
            + "beasts to those who know the words."),
        ["shadow"] = ("#6b1f3a", "Necromantic Tome",
            "Necromantic magic is taken, at a price. Necromancers see by the dark and work in it; blackguards turn "
            + "it to fury and blood. Its rituals drain life, call shadows, wear the shapes of wolves and bats, and "
            + "ask a great deal of those who use them."),
    };

    private readonly GameData _data;
    private readonly GameSession? _game;

    public GrimoireViewModel(GameData data, GameSession? game)
    {
        _data = data;
        _game = game;
        Realms = [.. data.Realms.Select(MakeRealm).Where(r => r.Books.Count > 0)];
        // Open at your own realm, at the first of your books.
        _selectedRealm = Realms.FirstOrDefault(r => r.Id == game?.Player.Class?.Realm) ?? Realms.FirstOrDefault();
        var cls = game?.Player.Class;
        Subtitle = cls?.Realm is null
            ? $"{Realms.Sum(r => r.Books.Count)} books and {data.Spells.Select(s => s.Name).Distinct().Count()} spells in {Realms.Count} realms."
            : $"Your class, the {cls.Name}, reads the {Realms.FirstOrDefault(r => r.Id == cls.Realm)?.Name ?? cls.Realm} books; its entries are marked ★.";
    }

    public string Title => "The Grimoire";

    /// <summary>The parchment the pages are printed on (plain paper colour if the picture is missing).</summary>
    public IBrush PageBackground { get; } = GrimoireArt.Parchment is { } paper
        ? new ImageBrush(paper) { TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 512, 512, RelativeUnit.Absolute) }
        : new SolidColorBrush(Color.FromRgb(0xE2, 0xD0, 0xA8));
    public string Subtitle { get; }
    public IReadOnlyList<GrimoireRealm> Realms { get; }
    [ObservableProperty] private GrimoireRealm? _selectedRealm;

    /// <summary>Words to look for in every book (a spell's name or what it does).</summary>
    [ObservableProperty] private string _search = "";
    public ObservableCollection<GrimoireSpell> SearchResults { get; } = [];
    public bool IsSearching => Search.Trim().Length > 0;
    public bool NoResults => IsSearching && SearchResults.Count == 0;

    partial void OnSearchChanged(string value)
    {
        SearchResults.Clear();
        var words = value.Trim();
        if (words.Length > 0)
            foreach (var spell in Realms.SelectMany(r => r.Books).SelectMany(b => b.Spells)
                         .Where(s => s.Name.Contains(words, StringComparison.OrdinalIgnoreCase)
                                     || s.Description.Contains(words, StringComparison.OrdinalIgnoreCase)))
                SearchResults.Add(spell);
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(NoResults));
    }

    [RelayCommand]
    private void ClearSearch() => Search = "";

    private GrimoireRealm MakeRealm(RealmDef realm)
    {
        var style = RealmStyle.GetValueOrDefault(realm.Id, ("#5a4a3a", "Book", ""));
        var books = _data.Spells.Where(s => s.Realm == realm.Id).Select(s => s.Book).Distinct()
            .Select(id => (Id: id, Kind: _data.Object(id)))
            .OrderBy(b => SoldIn(b.Id) is null ? 1 : 0).ThenBy(b => b.Kind?.Level ?? 0)
            .Select(b => MakeBook(b.Id, b.Kind, realm, style))
            .ToList();
        return new GrimoireRealm(realm.Id, realm.Name, GrimoireArt.Realm(realm.Id), style.Item1, books);
    }

    private GrimoireBook MakeBook(string id, ObjectKindDef? kind, RealmDef realm, (string Accent, string Kind, string Intro) style)
    {
        var spells = _data.Spells.Where(s => s.Book == id).ToList();
        var name = (kind?.Name ?? id).Trim('[', ']', ' ');
        var readers = AllClasses.Where(c => spells.Any(s => s.Classes.ContainsKey(c.SpellClass ?? c.Id))).Select(c => c.Name).ToList();
        var where = SoldIn(id) is { } store
            ? $"Sold in town, at the {store}."
            : $"Found in the dungeon, from about {Math.Max(1, kind?.MinDepth ?? kind?.Level ?? 1) * _data.Constants.FeetPerLevel} ft.";
        var you = _game?.Player.Class;
        var forYou = you is not null && spells.Any(s => s.Classes.ContainsKey(you.SpellClass ?? you.Id));
        return new GrimoireBook(id, name, $"{style.Kind} · {realm.Name} magic", GrimoireArt.Book(id), where,
            "Read by " + string.Join(", ", readers) + ".",
            [.. spells.Select(s => MakeSpell(s, name, style.Accent))], style.Accent,
            style.Intro.Length > 0 ? style.Intro[..1] : "", style.Intro.Length > 0 ? style.Intro[1..] : "", forYou);
    }

    /// <summary>Angband's nine classes and AVABand's multiclasses (each casting as its caster half).</summary>
    private IEnumerable<ClassDef> AllClasses => _data.Classes.Concat(_data.Multiclasses);

    private GrimoireSpell MakeSpell(SpellDef spell, string bookName, string accent)
    {
        var you = _game?.Player.Class;
        var lines = new List<GrimoireClassLine>();
        foreach (var cls in AllClasses)
        {
            if (!spell.Classes.TryGetValue(cls.SpellClass ?? cls.Id, out var raw)) continue;
            var info = Multiclass.Adjust(raw, cls);
            var yours = you is not null && cls.Id == you.Id;
            lines.Add(new GrimoireClassLine(
                $"{(yours ? "★ " : "")}{cls.Name}: level {info.Level} · {info.Mana} mana · {info.Fail}% base failure · {info.Exp * info.Level} exp the first time",
                yours));
        }
        var note = "";
        if (_game is not null && you is not null && spell.Classes.ContainsKey(you.SpellClass ?? you.Id))
        {
            var level = _game.SpellInfo(spell)?.Level ?? 0;
            note = _game.Player.LearnedSpells.Contains(spell.Id) ? "You know this one."
                : _game.Player.Level >= level ? "You could learn this now (G, with its book)."
                : $"You can learn this at level {level}.";
        }
        return new GrimoireSpell(spell.Name, GrimoireArt.Spell(spell.Name), spell.Description, lines, note, bookName, accent);
    }

    /// <summary>The shop that sells a book, if any ("Bookseller").</summary>
    private string? SoldIn(string bookId) =>
        _data.Stores.FirstOrDefault(s => !s.Id.Contains("black", StringComparison.Ordinal) && (s.Staples.Contains(bookId) || s.Stocked.Contains(bookId)))
            is { } store ? Capitalize(store.Id.Replace('_', ' ')) : null;

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

public sealed partial class MainWindowViewModel
{
    public event Action<GrimoireViewModel>? GrimoireRequested;

    public GrimoireViewModel CreateGrimoire() => new(_data, _game);

    /// <summary>Help → The Grimoire…: every spell in every book.</summary>
    [RelayCommand]
    public void ShowGrimoire() => GrimoireRequested?.Invoke(CreateGrimoire());
}
