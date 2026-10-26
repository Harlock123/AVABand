using Angband.Core.Game;
using Angband.Data.Tiles;

namespace Angband.Avalonia.ViewModels;

// AVABand's quests in the UI: whatever a quest asks (at the Prancing Pony, a sealed door, a forge,
// a shop) is shown as a menu, its words above the choices; the choice goes back to the game as a
// command, so it is recorded and replayed like any other. A choice may have a picture beside it (the
// notice board's postings: the monster to hunt, the thing to bring).
public sealed partial class MainWindowViewModel
{
    private void OnQuestPrompt(QuestPromptEvent prompt) =>
        ShowMenu(prompt.Title, [.. prompt.Choices.Select(c => (c.Label, StrongroomAction(c.Id) ?? (() => Execute(new QuestChoiceCommand(c.Id)))))], prompt.Text,
            prompt.Choices.Any(c => c.Picture is not null) ? [.. prompt.Choices.Select(c => ChoicePicture(c.Picture))] : null);

    /// <summary>A quest choice's picture ("monster:id" or "object:id") as a map cell on a lit floor.</summary>
    private IMapSource? ChoicePicture(string? picture)
    {
        if (picture?.Split(':', 2) is not [var what, var id]) return null;
        var floor = _cells.Terrain(_data.Terrain["floor"], TileLighting.Lit);
        if (what == "monster" && _data.Monster(id) is { } race) return new SingleCellSource(_cells.Monster(race, floor));
        if (what == "object" && _data.Object(id) is { } kind && _data.ObjectBase(kind.Base) is { } b)
            return new SingleCellSource(_cells.ObjectKind(kind, b, floor));
        return null;
    }
}
