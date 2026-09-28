namespace Angband.Avalonia.ViewModels;

/// <summary>One choice of the game-over menu, with the letter that picks it.</summary>
public sealed record GameOverChoice(string Letter, string Label, Action Act);

/// <summary>
/// What to do when a character's adventure is over — straight after a death, or on starting the
/// game with no living character to continue: play the same character again, make a new one, load
/// another, look at the scores or the dead character's sheet, or leave.
/// </summary>
public sealed class GameOverMenuViewModel(string title, IReadOnlyList<string> lines, IReadOnlyList<GameOverChoice> choices)
{
    public string Title { get; } = title;
    public IReadOnlyList<string> Lines { get; } = lines;
    public IReadOnlyList<GameOverChoice> Choices { get; } = choices;

    /// <summary>Raised when a choice is made, before it is acted on (the window closes).</summary>
    public event Action? Chosen;

    public void Choose(GameOverChoice choice)
    {
        Chosen?.Invoke();
        choice.Act();
    }

    /// <summary>A letter picks its choice; false if no choice has it.</summary>
    public bool ChooseLetter(char letter)
    {
        var choice = Choices.FirstOrDefault(c => c.Letter[0] == char.ToLowerInvariant(letter));
        if (choice is null) return false;
        Choose(choice);
        return true;
    }
}
