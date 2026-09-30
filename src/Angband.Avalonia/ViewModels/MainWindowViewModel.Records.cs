using Angband.Core.Game;
using Angband.Core.Records;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

// Character dumps and the high-score table. Death records the score and writes a dump automatically.
public sealed partial class MainWindowViewModel
{
    private RecordStore? _records;
    private ScoreEntry? _lastDeath;

    /// <summary>Raised to show the character sheet or the score table (the view opens a window).</summary>
    public event Action<CharacterSheetViewModel>? CharacterSheetRequested;
    public event Action<HighScoresViewModel>? HighScoresRequested;

    public void UseRecords(RecordStore records)
    {
        _records = records;
        UseLore(records.LoadLore());
        _featBook = records.LoadFeats();
    }

    private bool _confirmRetire;

    /// <summary>Q / Game → Retire: a winner may end the game (asks first).</summary>
    [RelayCommand]
    public void Retire()
    {
        if (!_game.Player.IsWinner || _game.Player.IsDead)
        {
            AddMessage("You can retire once you have defeated Morgoth.");
            return;
        }
        _confirmRetire = true;
        IsConfirming = true;
        LastMessage = "Retire from adventuring? Your game will end. (y/n)";
    }

    /// <summary>
    /// The time the character dump is stamped with: the clock, except when the README screenshots
    /// fix it so the pictures don't change from one run to the next.
    /// </summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>The dump text, with the recent messages oldest first.</summary>
    public string BuildDump() => CharacterDump.Build(_game, Messages.Take(20).Reverse(), Clock());

    [RelayCommand]
    public void ShowCharacterSheet()
    {
        var p = _game.Player;
        var sheet = new CharacterSheetViewModel($"{p.Name} the {p.Race?.Name} {p.Class?.Name}", BuildDump(),
            _records is null ? null : () => _records.WriteDump(_game, BuildDump()))
        {
            PaperDoll = CreatePaperDoll(),
        };
        _openSheets.Add(sheet);
        CharacterSheetRequested?.Invoke(sheet);
    }

    /// <summary>Character sheets on screen, whose paper dolls follow the equipment.</summary>
    private readonly List<CharacterSheetViewModel> _openSheets = [];

    /// <summary>A character sheet was closed: its doll no longer needs keeping up to date.</summary>
    public void SheetClosed(CharacterSheetViewModel sheet) => _openSheets.Remove(sheet);

    /// <summary>
    /// Keeps open character sheets' paper dolls current: rebuilt when an item is put on or taken
    /// off, or changes (an inscription, charges, a rune learned), when hit points change the
    /// character's colour, or when tiles, tileset or font size change.
    /// </summary>
    private void RefreshPaperDolls()
    {
        if (_openSheets.Count == 0) return;
        var signature = PaperDollSignature();
        foreach (var sheet in _openSheets)
            if (sheet.PaperDoll is { } doll && doll.Signature != signature)
                sheet.PaperDoll = CreatePaperDoll();
    }

    private string PaperDollSignature()
    {
        var p = _game.Player;
        var items = p.Inventory.Equipment.Select(i => i is null ? "-" : $"{i.Serial}:{_game.Describe(i)}");
        return string.Join("|", items) + $"|{p.Name}|{p.Hp}/{p.MaxHp}|{UseTiles}|{SelectedTileset?.Id}|{MapFontSize}";
    }

    /// <summary>The paper doll for the character sheet: what is worn where, drawn as on the map.</summary>
    public PaperDollViewModel CreatePaperDoll()
    {
        var floor = _cells.Terrain(_data.Terrain["floor"], Angband.Data.Tiles.TileLighting.Lit);
        var equipment = _game.Player.Inventory.Equipment;
        PaperDollSlot Slot(int index, string label)
        {
            var item = equipment[index];
            if (item is null) return Drawn(new PaperDollSlot(label, null, "(nothing)", $"{label}: nothing worn", new SingleCellSource(MapCell.Unknown)));
            var name = _game.Describe(item);
            return Drawn(new PaperDollSlot(label, item, name, name, new SingleCellSource(_cells.Object(item, 1, _game.Knowledge, floor))));
        }
        PaperDollSlot Drawn(PaperDollSlot slot) => slot with { UseTiles = UseTiles, Tileset = SelectedTileset, FontSize = MapFontSize };
        var p = _game.Player;
        // Indices follow Inventory.Slots: weapon, bow, ring (left), ring (right), amulet, light,
        // body, cloak, shield, head, hands, feet.
        return new PaperDollViewModel
        {
            Weapon = Slot(0, "Weapon"), Bow = Slot(1, "Bow"), RingLeft = Slot(2, "Left ring"), RingRight = Slot(3, "Right ring"),
            Amulet = Slot(4, "Amulet"), Light = Slot(5, "Light"), Body = Slot(6, "Body"), Cloak = Slot(7, "Cloak"),
            Shield = Slot(8, "Shield"), Head = Slot(9, "Head"), Hands = Slot(10, "Hands"), Feet = Slot(11, "Feet"),
            Player = Drawn(new PaperDollSlot($"{p.Race?.Name} {p.Class?.Name}", null, p.Name, $"{p.Name} the {p.Race?.Name} {p.Class?.Name}",
                new SingleCellSource(_cells.Player(floor, p.Hp, p.MaxHp)))) with { IsCharacter = true },
            PlayerName = p.Name,
            Signature = PaperDollSignature(),
        };
    }

    [RelayCommand]
    public void ShowHighScores() => HighScoresRequested?.Invoke(CreateHighScores());

    public HighScoresViewModel CreateHighScores()
    {
        var board = _records?.LoadScores() ?? new ScoreBoard();
        var living = _game.Player.IsDead ? null : ScoreEntry.For(_game);
        var highlight = _lastDeath is null ? null
            : board.Entries.FirstOrDefault(e => e.Seed == _lastDeath.Seed && e.Name == _lastDeath.Name && e.DateUtc == _lastDeath.DateUtc);
        return new HighScoresViewModel(board, living, highlight);
    }

    /// <summary>On death: enter the score table and write a dump (Angband's tombstone and death dump).</summary>
    /// <returns>The line about the score, for the game-over menu (null without a record store).</returns>
    private string? RecordDeath()
    {
        if (_records is null) return null;
        var entry = ScoreEntry.For(_game);
        SaveLore();
        try
        {
            // Angband enter_score: cheaters are not scored.
            var rank = _game.IsCheater ? 0 : _records.RecordScore(entry);
            _lastDeath = entry;
            var dump = _records.WriteDump(_game, BuildDump());
            var score = _game.IsCheater ? "Score not registered for cheaters."
                : rank > 0
                ? $"You placed #{rank} on the high-score table ({entry.Points} points)."
                : $"You scored {entry.Points} points.";
            AddMessage(score);
            AddMessage($"Character dump written to {dump}");
            return score;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddMessage($"Could not record the score: {ex.Message}");
            return null;
        }
    }
}
