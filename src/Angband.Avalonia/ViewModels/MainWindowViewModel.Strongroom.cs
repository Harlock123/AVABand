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
        if (strongroom is StrongroomStore store)
        {
            // Which save to check, after a crash, for whether a change reached it.
            store.CurrentSave = () => _saves is not null && !_game.IsTutorial && !_game.IsReplay ? _saves.PathFor(_game) : null;
            store.Recover(); // (a game that stopped between the lockers and the save: made final, or undone)
        }
    }

    /// <summary>The character saved (or not saveable): the strongroom's change is final.</summary>
    private void CommitStrongroom()
    {
        TrySave();
        if (_strongroom is StrongroomStore store) store.Commit();
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
                CommitStrongroom();
            };
        if (choiceId.StartsWith("strongroom:store:", StringComparison.Ordinal))
            return () =>
            {
                Execute(new QuestChoiceCommand(choiceId));
                CommitStrongroom();
            };
        return null;
    }
}
