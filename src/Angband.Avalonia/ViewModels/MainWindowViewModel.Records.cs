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

    /// <summary>The dump text, with the recent messages oldest first.</summary>
    public string BuildDump() => CharacterDump.Build(_game, Messages.Take(20).Reverse());

    [RelayCommand]
    public void ShowCharacterSheet()
    {
        var p = _game.Player;
        var sheet = new CharacterSheetViewModel($"{p.Name} the {p.Race?.Name} {p.Class?.Name}", BuildDump(),
            _records is null ? null : () => _records.WriteDump(_game, BuildDump()));
        CharacterSheetRequested?.Invoke(sheet);
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
    private void RecordDeath()
    {
        if (_records is null) return;
        var entry = ScoreEntry.For(_game);
        SaveLore();
        try
        {
            // Angband enter_score: cheaters are not scored.
            var rank = _game.IsCheater ? 0 : _records.RecordScore(entry);
            _lastDeath = entry;
            var dump = _records.WriteDump(_game, CharacterDump.Build(_game, Messages.Take(20).Reverse()));
            AddMessage(_game.IsCheater ? "Score not registered for cheaters."
                : rank > 0
                ? $"You placed #{rank} on the high-score table ({entry.Points} points). Ctrl+H shows it."
                : $"You scored {entry.Points} points.");
            AddMessage($"Character dump written to {dump}");
            ShowHighScores();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddMessage($"Could not record the score: {ex.Message}");
        }
    }
}
