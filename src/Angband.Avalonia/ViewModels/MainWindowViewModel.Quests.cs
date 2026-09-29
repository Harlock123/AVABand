using Angband.Core.Game;

namespace Angband.Avalonia.ViewModels;

// AVABand's quests in the UI: whatever a quest asks (at the Prancing Pony, a sealed door, a forge,
// a shop) is shown as a menu, its words above the choices; the choice goes back to the game as a
// command, so it is recorded and replayed like any other.
public sealed partial class MainWindowViewModel
{
    private void OnQuestPrompt(QuestPromptEvent prompt) =>
        ShowMenu(prompt.Title, [.. prompt.Choices.Select(c => (c.Label, (Action)(() => Execute(new QuestChoiceCommand(c.Id)))))], prompt.Text);
}
