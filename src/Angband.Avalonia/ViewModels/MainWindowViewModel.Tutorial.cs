using Angband.Core.Game;
using Angband.Input;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

// The tutorial (AVABand's own; Game → Tutorial): a short level made for teaching (Core's Tutorial),
// with the hint banner saying what to do at each step, in the keys you actually have. It is never
// saved or scored; the stairs at the end finish it, with a menu to go on to a real character.
public sealed partial class MainWindowViewModel
{
    private TutorialStep? _tutorialShown;
    private bool _tutorialDismissed;

    /// <summary>Starts the tutorial (keeping the character in play saved first).</summary>
    [RelayCommand]
    public void StartTutorial()
    {
        if (!_game.Player.IsDead && !_game.IsTutorial) TrySave();
        AttachGame(Tutorial.Create(_data, (ulong)Environment.TickCount64));
        _tutorialShown = null;
        _tutorialDismissed = false;
        LastMessage = "The tutorial: a short level to learn the basics. Nothing here is saved or scored.";
        Refresh();
    }

    /// <summary>What to do at each step of the tutorial.</summary>
    private string TutorialText(TutorialStep step) => step switch
    {
        TutorialStep.PickUp => "Welcome to the tutorial! Move with the arrow keys, the keypad or hjklyubn (or click a square). "
                               + $"Walk onto the potion (!) and pick it up with {KeyName(InputAction.Pickup)}.",
        TutorialStep.OpenDoor => $"Now head east. Walk into the door (+) to open it, or press {KeyName(InputAction.Open)} and a direction.",
        TutorialStep.Trap => "A trap (^) lies in the corridor, with no way round. Walk at it to disarm it (keep trying if you fail), "
                             + $"or press {KeyName(InputAction.WalkIntoTrap)} to jump onto it on purpose.",
        TutorialStep.Fight => "A kobold (k) sleeps in the next room. Walk into it to attack it. "
                              + $"If you get hurt, quaff a potion ({KeyName(InputAction.Quaff)}). {KeyName(InputAction.Look)} looks at it first: "
                              + "once you know its attacks, what you recall of it ends with how dangerous it is to you.",
        TutorialStep.Recall => "Well fought! Through the next door lies a Scroll of Word of Recall (?). Pick it up and read it "
                               + $"({KeyName(InputAction.Read)}): it's how you get home from the deep, and back down again.",
        TutorialStep.Inn => "The door marked 9 is the Prancing Pony, the inn in town (put here just for the lesson). "
                            + "Walk onto it to see what it offers: quests, and work on its notice board.",
        TutorialStep.Shop => "The door marked 2 is the Armoury (here for the lesson). Walk in: under each thing for sale a note says "
                             + "whether it would suit you — Better, Worse or Mixed, and why. When a shop can do something more for you, a Services "
                             + "button (or !) shows: the Alchemist identifies things, the Armoury takes gems out of bracers. Escape leaves.",
        TutorialStep.Tidy => "Your pack fills as you go, and says (full) when there's no room left. Tidy it now (Game → Tidy pack, "
                             + "or right-click yourself, then Other): stacks that belong together merge, and it tells you how full it is.",
        TutorialStep.Knowledge => $"Now open the Knowledge screen ({KeyName(InputAction.MonsterKnowledge)}): all you've learned, your quest "
                                  + "journal (Quests), and milestones across all your characters (Feats). Close it again with Escape.",
        _ => $"That's everything! Go through the last door, stand on the stairs (>) and press {KeyName(InputAction.StairsDown)} to finish.",
    };

    /// <summary>In the tutorial, the banner shows the current step (until dismissed, then again at the next).</summary>
    private bool CheckTutorial()
    {
        if (!_game.IsTutorial) return false;
        var step = Tutorial.StepOf(_game);
        if (step != _tutorialShown)
        {
            _tutorialShown = step;
            _tutorialDismissed = false;
        }
        HintText = _tutorialDismissed || _game.Player.IsDead ? "" : TutorialText(step);
        return true;
    }

    /// <summary>Dying in the tutorial: no score, no save to delete; try again, or go on.</summary>
    private bool TutorialDeath()
    {
        if (!_game.IsTutorial) return false;
        HintText = "";
        GameOverMenuRequested?.Invoke(new GameOverMenuViewModel("The tutorial ended early", [
            $"{_game.Player.Name} was killed by {_game.Player.KilledBy}. In the tutorial that costs nothing.",
        ], [.. Choices(null, () => { }, sheet: false).Prepend(new GameOverChoice("", "Try the tutorial again", StartTutorial))
            .Select((c, i) => c with { Letter = ((char)('a' + i)).ToString() })]));
        return true;
    }

    /// <summary>Taking the stairs down ends the tutorial (there is no second level).</summary>
    private bool TutorialStairs(GameCommand command)
    {
        if (!_game.IsTutorial || command is not TakeStairsCommand { Down: true }
            || !_game.Level.Has(_game.Player.Position, Angband.Core.Definitions.TerrainFlags.DownStair)) return false;
        HintText = "";
        GameOverMenuRequested?.Invoke(new GameOverMenuViewModel("Tutorial complete", [
            "You know the basics: moving, picking up, doors, traps, fighting, Word of Recall, the inn's quests, shops, a tidy pack, Knowledge and stairs.",
            "The hints for new players go on showing tips as new things happen. Good luck!",
        ], [.. Choices(null, () => { }, sheet: false).Prepend(new GameOverChoice("", "Play the tutorial again", StartTutorial))
            .Select((c, i) => c with { Letter = ((char)('a' + i)).ToString() })]));
        return true;
    }
}
