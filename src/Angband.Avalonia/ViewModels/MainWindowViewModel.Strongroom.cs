using Angband.Core.Game;

namespace Angband.Avalonia.ViewModels;

// Butterbur's strongroom in the UI: the lockers file is handed to each game; taking a locker's item out
// becomes a command carrying the item (so a replay has it without the file); and the character is saved
// at once after leaving or taking something, so the lockers file and the save stay in step.
public sealed partial class MainWindowViewModel
{
    private IStrongroom? _strongroom;

    /// <summary>Uses a strongroom (the lockers shared by all your characters).</summary>
    public void UseStrongroom(IStrongroom strongroom)
    {
        _strongroom = strongroom;
        _game.Strongroom = strongroom;
    }

    /// <summary>A strongroom choice from the inn's menu: taking a locker's item out, or anything else there (then saving).</summary>
    private Action? StrongroomAction(string choiceId)
    {
        if (choiceId.StartsWith("strongroom-take:", StringComparison.Ordinal))
            return () =>
            {
                var id = choiceId["strongroom-take:".Length..];
                if (_strongroom?.Lockers.FirstOrDefault(l => l.Id == id) is not { } locker)
                {
                    AddMessage("That locker stands empty.");
                    return;
                }
                Execute(new TakeFromLockerCommand(locker.Id, locker.ItemJson, locker.Value));
                TrySave();
            };
        if (choiceId.StartsWith("strongroom:store:", StringComparison.Ordinal))
            return () =>
            {
                Execute(new QuestChoiceCommand(choiceId));
                TrySave();
            };
        return null;
    }
}
