using Angband.Core.Records;

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

    /// <summary>After a death: what happened (the last hurts, what went unused, a tip); null otherwise.</summary>
    public GameOverRecap? Recap { get; init; }

    /// <summary>Whether there is a recap to show.</summary>
    public bool HasRecap => Recap is not null;

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

/// <summary>
/// AVABand's own death recap as the game-over window shows it: the last few hurts, the escapes and
/// healing still in the pack, and one tip.
/// </summary>
public sealed class GameOverRecap
{
    /// <summary>The last hurts, oldest first, a line each ("A Cave orc: 12 damage, 3 hit points left").</summary>
    public required IReadOnlyList<string> Hurts { get; init; }

    /// <summary>What went unused: a line for escapes and one for healing (none if nothing did).</summary>
    public required IReadOnlyList<string> Unused { get; init; }

    /// <summary>The tip.</summary>
    public required string Tip { get; init; }

    /// <summary>Whether there are hurts to list (a game loaded just before dying may have none).</summary>
    public bool HasHurts => Hurts.Count > 0;

    /// <summary>Whether anything went unused.</summary>
    public bool HasUnused => Unused.Count > 0;

    /// <summary>The recap of the engine's <see cref="DeathRecap"/>.</summary>
    public static GameOverRecap From(DeathRecap recap)
    {
        var unused = new List<string>();
        if (recap.Escapes.Count > 0) unused.Add($"Escapes you didn't use: {string.Join(", ", recap.Escapes)}.");
        if (recap.Healing.Count > 0) unused.Add($"Healing you didn't use: {string.Join(", ", recap.Healing)}.");
        return new GameOverRecap { Hurts = [.. recap.Hurts.Select(DeathRecap.HurtLine)], Unused = unused, Tip = recap.Tip };
    }
}
