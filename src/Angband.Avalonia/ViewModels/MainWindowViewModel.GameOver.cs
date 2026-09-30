using Angband.Core.Game;
using Angband.Core.Records;

namespace Angband.Avalonia.ViewModels;

// The game-over menu: after a death (or a winner's retirement), and on starting with no living
// character to continue — rather than quietly starting a new game in town.
public sealed partial class MainWindowViewModel
{
    /// <summary>Raised when the game-over menu should open (the view shows it).</summary>
    public event Action<GameOverMenuViewModel>? GameOverMenuRequested;

    /// <summary>Raised by the menu's Exit (the view closes the app).</summary>
    public event Action? ExitRequested;

    /// <summary>
    /// Set at start-up when there was a character but none is alive to continue (the last one died):
    /// the window then offers the menu instead of just starting over in town.
    /// </summary>
    public bool ShouldOfferStartMenu { get; private set; }

    /// <summary>After a death: how it ended, and what next.</summary>
    private void ShowDeathMenu(string? scoreLine)
    {
        var p = _game.Player;
        var who = $"{p.Name} the {p.Race?.Name} {p.Class?.Name}";
        var lines = new List<string>
        {
            p.IsWinner
                ? $"{who} retired, victorious, at character level {p.Level}."
                : $"{who} was killed by {p.KilledBy} {Where(p.Depth)}, at character level {p.Level}.",
        };
        if (scoreLine is not null) lines.Add(scoreLine);
        var again = SpecOf(_game);
        GameOverMenuRequested?.Invoke(new GameOverMenuViewModel(p.IsWinner ? "Your adventure is over" : "You have died",
            lines, Choices(again, () => { if (again is not null) StartCharacter(again); }, sheet: true)));
    }

    /// <summary>At start-up with no living character: how the last one ended, and what next.</summary>
    public void ShowStartMenu()
    {
        ShouldOfferStartMenu = false;
        var last = _records?.LoadScores().Entries.OrderByDescending(e => e.DateUtc).FirstOrDefault();
        var lines = new List<string>
        {
            last is { IsAlive: false } dead
                ? $"Your last adventure ended: {dead.Name} the {dead.Race} {dead.Class} was killed by {dead.KilledBy} {Where(dead.Depth)}."
                : "There is no character to continue.",
        };
        // A fresh game with the last character is already under way behind the menu: playing it is just closing.
        GameOverMenuRequested?.Invoke(new GameOverMenuViewModel("Welcome back", lines,
            Choices(LastCharacter(), () => AddMessage("A new adventure begins."), sheet: false)));
    }

    private static string Where(int depth) => depth == 0 ? "in the town" : $"at {depth * 50} ft (level {depth})";

    /// <summary>
    /// The character a game was played with, to play again: the one last made on the creation screen
    /// if it is this one (its stats as rolled or bought), otherwise rebuilt from the player (name, race,
    /// class, birth stats and birth options).
    /// </summary>
    private CharacterSpec? SpecOf(GameSession game)
    {
        var p = game.Player;
        if (p.Class is null) return null;
        if (LastCharacter() is { } last && last.Name == p.Name && last.ClassId == p.Class.Id && last.RaceId == (p.Race?.Id ?? last.RaceId))
            return last;
        var stats = p.BaseStats.Count > 0 ? new Dictionary<string, int>(p.BaseStats) : CharacterSpec.Default("human", p.Class.Id).BaseStats;
        return new CharacterSpec(p.Name, p.Race?.Id ?? "human", p.Class.Id, stats, p.HeroicBirth ? StatMethod.HeroicRoll : StatMethod.Roll,
            game.Options.OfKind(Angband.Core.Game.OptionKind.Birth));
    }

    private List<GameOverChoice> Choices(CharacterSpec? again, Action playAgain, bool sheet)
    {
        var options = new List<(string Label, Action Act)>();
        if (again is { } spec)
            options.Add(($"Play again as {spec.Name} the {_data.Race(spec.RaceId)?.Name} {_data.Class(spec.ClassId)?.Name}", playAgain));
        options.Add(("Create a new character...", RequestNewCharacter));
        if (_saves?.List().Any(e => !e.Summary.IsDead) == true) options.Add(("Load a saved character...", () => LoadRequested?.Invoke()));
        if (_records is not null) options.Add(("View the high scores", ShowHighScores));
        if (sheet) options.Add(("Look at the character sheet", ShowCharacterSheet));
        options.Add(("Exit", () => ExitRequested?.Invoke()));
        return [.. options.Select((o, i) => new GameOverChoice(((char)('a' + i)).ToString(), o.Label, o.Act))];
    }
}
