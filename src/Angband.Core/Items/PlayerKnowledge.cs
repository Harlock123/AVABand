using Angband.Core.Definitions;
using Angband.Core.Randomness;

namespace Angband.Core.Items;

/// <summary>
/// What the player has learned about objects this game (Angband 4.2 rune-based identification):
/// runes (properties such as "+to-hit" or "resist fire") are learned once and then recognised on
/// every object, and flavoured kinds (potions, scrolls...) become known once used. Flavour
/// appearances are shuffled per game from the seed.
/// </summary>
public sealed class PlayerKnowledge
{
    private readonly HashSet<string> _runes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _awareKinds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _triedKinds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FlavorDef> _flavors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenKinds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenEgos = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenArtifacts = new(StringComparer.Ordinal);

    /// <summary>Object kinds seen this game (Angband kind->everseen).</summary>
    public IReadOnlyCollection<string> SeenKinds => _seenKinds;
    /// <summary>Egos identified this game (seen on a fully known object).</summary>
    public IReadOnlyCollection<string> SeenEgos => _seenEgos;
    /// <summary>Artifacts found and identified this game.</summary>
    public IReadOnlyCollection<string> SeenArtifacts => _seenArtifacts;

    /// <summary>Auto-inscriptions, by kind (Angband's kind notes).</summary>
    public IReadOnlyDictionary<string, string> KindNotes => _kindNotes;
    private readonly Dictionary<string, string> _kindNotes = new(StringComparer.Ordinal);

    public string? KindNote(ObjectKindDef kind) => _kindNotes.GetValueOrDefault(kind.Id);

    public void SetKindNote(ObjectKindDef kind, string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) _kindNotes.Remove(kind.Id);
        else _kindNotes[kind.Id] = note.Trim();
    }

    public void RestoreKindNotes(IReadOnlyDictionary<string, string> notes)
    {
        foreach (var (k, v) in notes) _kindNotes[k] = v;
    }

    /// <summary>
    /// Notices an object: its kind, and — once it is fully known — its ego or artifact are recorded
    /// as seen (Angband object_see bookkeeping), and an uninscribed one takes its kind's auto-inscription.
    /// </summary>
    public void See(Item item)
    {
        if (item.Note is null && _kindNotes.TryGetValue(item.Kind.Id, out var note)) item.Note = note;
        _seenKinds.Add(item.Kind.Id);
        if (!IsFullyKnown(item)) return;
        if (item.Ego is { } ego) _seenEgos.Add(ego.Id);
        if (item.Artifact is { } art) _seenArtifacts.Add(art.Id);
    }

    public void RestoreSeen(IEnumerable<string> kinds, IEnumerable<string> egos, IEnumerable<string> artifacts)
    {
        _seenKinds.UnionWith(kinds);
        _seenEgos.UnionWith(egos);
        _seenArtifacts.UnionWith(artifacts);
    }

    /// <summary>The chest traps there are (for describing chests).</summary>
    public IReadOnlyList<ChestTrapDef> ChestTraps { get; }

    public PlayerKnowledge(GameData data, ulong seed)
    {
        ChestTraps = data.ChestTraps;
        var rng = new GameRandom(GameRandom.DeriveSeed(seed, 0xF1A7));
        foreach (var group in data.Flavors)
        {
            var kinds = data.Objects
                .Where(k => data.ObjectBase(k.Base)?.Flavor == group.Id && !k.IsSpecialArtifactKind)
                .OrderBy(k => k.Id, StringComparer.Ordinal)
                .ToList();
            foreach (var fixedFlavor in group.Fixed)
                _flavors[fixedFlavor.Kind] = new FlavorDef { Name = fixedFlavor.Name, Color = fixedFlavor.Color };
            if (group.TitleWords.Count > 0)
            {
                var used = new HashSet<string>();
                foreach (var kind in kinds)
                {
                    string title;
                    do title = ScrollTitle(rng, group.TitleWords);
                    while (!used.Add(title));
                    _flavors[kind.Id] = new FlavorDef { Name = title, Color = "White" };
                }
                continue;
            }

            var flavors = group.Flavors.ToList();
            rng.Shuffle(flavors);
            for (var i = 0; i < kinds.Count; i++)
                _flavors[kinds[i].Id] = flavors[i % Math.Max(1, flavors.Count)];
        }
    }

    public IReadOnlyCollection<string> Runes => _runes;
    public IReadOnlyCollection<string> AwareKinds => _awareKinds;
    public IReadOnlyCollection<string> TriedKinds => _triedKinds;
    /// <summary>Kind id to flavour, as assigned this game.</summary>
    public IReadOnlyDictionary<string, FlavorDef> Flavors => _flavors;

    /// <summary>Replaces everything learned with saved knowledge.</summary>
    public void Restore(IEnumerable<string> runes, IEnumerable<string> aware, IEnumerable<string> tried,
        IReadOnlyDictionary<string, FlavorDef> flavors)
    {
        _runes.Clear(); _runes.UnionWith(runes);
        _awareKinds.Clear(); _awareKinds.UnionWith(aware);
        _triedKinds.Clear(); _triedKinds.UnionWith(tried);
        foreach (var (kind, flavor) in flavors) _flavors[kind] = flavor;
    }

    public bool KnowsRune(string rune) => _runes.Contains(rune);

    /// <summary>The protections that are object flags in Angband (see invisible, free action...), not resistances.</summary>
    private static readonly HashSet<string> FlagProtections = ["fear", "blind", "conf", "stun", "free_act", "see_invis"];

    /// <summary>
    /// Whether a property of this object is known (Angband object_flags_known): its rune is, or —
    /// for an EASY_KNOW kind you know (lights, and the rings and amulets that are nothing more than
    /// their flavour) — it is one of its ego's object flags.
    /// </summary>
    public bool KnowsProperty(Item item, string rune)
    {
        if (_runes.Contains(rune)) return true;
        if (item.Ego is not { } ego || !(item.Kind.Has("EASY_KNOW") || item.Base.Flags.Contains("EASY_KNOW")) || !KnowsKind(item)) return false;
        var colon = rune.IndexOf(':');
        var what = colon < 0 ? "" : rune[(colon + 1)..];
        return (colon < 0 ? rune : rune[..colon]) switch
        {
            "flag" => ego.Flags.Contains(what),
            "resist" => FlagProtections.Contains(what) && ego.Resists.Contains(what),
            _ => false,
        };
    }

    /// <summary>Whether an object is marked for ignoring (shown as {ignore}); set by the game session.</summary>
    public Func<Item, bool>? IgnoredCheck { get; set; }

    /// <summary>Angband show_flavors: name identified flavoured objects with their flavour too.</summary>
    public bool ShowFlavors { get; set; }

    /// <summary>Learns a rune; true if it was new.</summary>
    public bool LearnRune(string rune) => _runes.Add(rune);

    public bool IsAware(ObjectKindDef kind) => _awareKinds.Contains(kind.Id);

    /// <summary>Learns what a flavoured kind is; true if it was new.</summary>
    public bool LearnKind(ObjectKindDef kind) => _awareKinds.Add(kind.Id);

    public bool HasTried(ObjectKindDef kind) => _triedKinds.Contains(kind.Id);
    public void MarkTried(ObjectKindDef kind) => _triedKinds.Add(kind.Id);

    public FlavorDef? Flavor(ObjectKindDef kind) => _flavors.GetValueOrDefault(kind.Id);

    /// <summary>The object's kind is recognised (always true for unflavoured kinds).</summary>
    public bool KnowsKind(Item item) => !item.IsFlavored || IsAware(item.Kind);

    public IEnumerable<string> UnknownRunes(Item item) => item.Runes().Where(r => !_runes.Contains(r));

    /// <summary>Everything about the object is known (Angband object_fully_known).</summary>
    public bool IsFullyKnown(Item item) => KnowsKind(item) && !UnknownRunes(item).Any();

    /// <summary>
    /// Angband flavor_init's scroll titles: made-up words of 2 to 8 letters, added while the title
    /// stays under 15 characters (its 18-character buffer, less the quotes and the end).
    /// </summary>
    private static string ScrollTitle(GameRandom rng, IReadOnlyList<string> words)
    {
        var parts = new List<string>();
        var length = 0;
        var word = RandomName.Make(rng, words, 2, 8);
        while (length + word.Length < 15)
        {
            parts.Add(word);
            length += word.Length + 1;
            word = RandomName.Make(rng, words, 2, 8);
        }
        return string.Join(' ', parts);
    }
}
