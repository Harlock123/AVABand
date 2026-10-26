using System.Collections.ObjectModel;
using System.Globalization;
using Angband.Core.Items;
using Angband.Core.Records;
using Angband.Data.Tiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>One piece in the museum, or the empty place where one will go.</summary>
public sealed record MuseumPiece(string Name, string Note, bool IsFound, IMapSource Picture, string Detail)
{
    public bool UseTiles { get; init; }
    public TilesetManifest? Tileset { get; init; }
    public double FontSize { get; init; } = 16;
    public double Opacity => IsFound ? 1.0 : 0.45;
    public bool IsMissing => !IsFound;
}

/// <summary>One room of the museum: artifacts, uniques or the bestiary.</summary>
public sealed partial class MuseumShelf : ObservableObject
{
    private readonly IReadOnlyList<MuseumPiece> _all;

    public MuseumShelf(string title, string unit, IReadOnlyList<MuseumPiece> pieces)
    {
        _all = pieces;
        Title = title;
        var found = pieces.Count(p => p.IsFound);
        Summary = $"{found} of {pieces.Count} {unit}";
        Fill();
    }

    public string Title { get; }
    public string Summary { get; }
    public ObservableCollection<MuseumPiece> Pieces { get; } = [];
    public int FoundCount => _all.Count(p => p.IsFound);

    /// <summary>Only what's in the museum (not the empty places).</summary>
    [ObservableProperty] private bool _onlyFound;

    [ObservableProperty] private MuseumPiece? _selected;

    partial void OnOnlyFoundChanged(bool value) => Fill();

    private void Fill()
    {
        Pieces.Clear();
        foreach (var p in _all.Where(p => !OnlyFound || p.IsFound)) Pieces.Add(p);
        Selected = Pieces.FirstOrDefault(p => p.IsFound) ?? Pieces.FirstOrDefault();
    }
}

/// <summary>
/// The museum (AVABand's own): every standard artifact, unique and kind of monster, those your
/// characters have found, slain or seen shown with who did it first, the rest as empty places.
/// </summary>
public sealed class MuseumViewModel(MuseumShelf artifacts, MuseumShelf uniques, MuseumShelf bestiary)
{
    public MuseumShelf Artifacts { get; } = artifacts;
    public MuseumShelf Uniques { get; } = uniques;
    public MuseumShelf Bestiary { get; } = bestiary;

    public string Summary =>
        $"{Count(Artifacts.FoundCount, "artifact")}  ·  {Count(Uniques.FoundCount, "unique")} slain  ·  "
        + $"{Bestiary.FoundCount} {(Bestiary.FoundCount == 1 ? "kind" : "kinds")} of monster seen";

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}

public sealed partial class MainWindowViewModel
{
    private Angband.Core.Records.Museum _museum = new();

    /// <summary>The museum's pieces so far (for tests).</summary>
    public Angband.Core.Records.Museum Museum => _museum;

    /// <summary>Raised to show the museum (the view opens its window).</summary>
    public event Action<MuseumViewModel>? MuseumRequested;

    /// <summary>Loads the museum; the first time, fills it from what came before it.</summary>
    private void UseMuseum(RecordStore records)
    {
        _museum = records.LoadMuseum();
        if (_museum.Backfilled) return;
        _museum.Backfill(_data, _lore, records.LoadGraveyard());
        records.SaveMuseum(_museum);
    }

    /// <summary>After a command: what's new for the museum is recorded (and an artifact or unique said).</summary>
    private void CheckMuseum()
    {
        var before = _museum.Artifacts.Count + _museum.Uniques.Count + _museum.Monsters.Count + _museum.CountBounties();
        foreach (var name in _museum.Check(_game)) AddMessage($"A first for the museum: {name}.");
        if (_museum.Artifacts.Count + _museum.Uniques.Count + _museum.Monsters.Count + _museum.CountBounties() != before) _records?.SaveMuseum(_museum);
    }

    [RelayCommand]
    public void ShowMuseum() => MuseumRequested?.Invoke(CreateMuseum());

    public MuseumViewModel CreateMuseum()
    {
        var floor = _cells.Terrain(_data.Terrain["floor"], TileLighting.Lit);
        MuseumPiece Drawn(MuseumPiece piece) => piece with { UseTiles = UseTiles, Tileset = SelectedTileset, FontSize = MapFontSize };
        static string When(MuseumEntry e) => e.WhenUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        string By(MuseumEntry e, string verb) =>
            e.Character.Length == 0 ? $"{verb} before the museum opened" : $"{verb} by {e.Character}{(e.Depth > 0 ? $" at {e.Depth * 50} ft" : "")}, {When(e)}";
        var missing = new SingleCellSource(MapCell.Unknown);

        var artifacts = _data.Artifacts.OrderBy(a => a.Level).ThenBy(a => a.Name, StringComparer.Ordinal).Select(a =>
        {
            var kind = _data.Object(a.Kind);
            var name = $"{(kind is null ? "" : Capitalize(ItemNaming.Plain(kind.Name, false)))} {a.Name}".Trim();
            if (!_museum.Artifacts.TryGetValue(a.Id, out var e))
                return Drawn(new MuseumPiece("?", $"level {a.Level}", false, missing,
                    $"An artifact of level {a.Level}, not yet found by any of your characters."));
            var picture = kind is not null && _data.ObjectBase(kind.Base) is { } b
                ? new SingleCellSource(_cells.ObjectKind(kind, b, floor)) : missing;
            return Drawn(new MuseumPiece(name, By(e, "Found"), true, picture,
                $"{name}\n\n{By(e, "Found")}.\n\n{(a.Description.Length > 0 ? a.Description : "(No tale is told of it.)")}"));
        }).ToList();

        var uniques = Angband.Core.Records.Museum.UniqueRaces(_data).OrderBy(r => r.Depth).ThenBy(r => r.Name, StringComparer.Ordinal).Select(r =>
            _museum.Uniques.TryGetValue(r.Id, out var e)
                ? Drawn(new MuseumPiece(r.Name, By(e, "Slain") + (e.Bounty ? " · Bounty claimed" : ""), true, new SingleCellSource(_cells.Monster(r, floor)),
                    $"{r.Name}\n\n{By(e, "Slain")}.{(e.Bounty ? " Bounty claimed." : "")}\n\n{_game.Recall(r)}"))
                : _museum.Monsters.ContainsKey(r.Id)
                    ? Drawn(new MuseumPiece(r.Name, "Seen, but never slain", false, new SingleCellSource(_cells.Monster(r, floor)),
                        $"{r.Name}\n\nSeen, but never slain.\n\n{_game.Recall(r)}"))
                    : Drawn(new MuseumPiece("?", $"{r.Depth * 50} ft", false, missing, $"A unique of {r.Depth * 50} ft, not yet met."))).ToList();

        var bestiary = Angband.Core.Records.Museum.Races(_data).OrderBy(r => r.Depth).ThenBy(r => r.Name, StringComparer.Ordinal).Select(r =>
            _museum.Monsters.TryGetValue(r.Id, out var e)
                ? Drawn(new MuseumPiece(r.Name, By(e, "First seen"), true, new SingleCellSource(_cells.Monster(r, floor)),
                    $"{r.Name}\n\n{By(e, "First seen")}.\n\n{_game.Recall(r)}"))
                : Drawn(new MuseumPiece("?", $"{r.Depth * 50} ft", false, missing, $"A monster of {r.Depth * 50} ft, not yet seen."))).ToList();

        return new MuseumViewModel(new MuseumShelf("Artifacts", "artifacts found", artifacts),
            new MuseumShelf("Uniques", "uniques slain", uniques), new MuseumShelf("Bestiary", "kinds of monster seen", bestiary));
    }
}
