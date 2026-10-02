using Angband.Core.Game;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>A step in a quest card: a tick for those behind you, a dot for the one at hand.</summary>
public sealed record QuestLogStepRow(string Mark, string Text, bool Done)
{
    public double TextOpacity => Done ? 0.6 : 1;
}

/// <summary>A quest's card in the quest log.</summary>
public sealed record QuestLogCard(string Name, string Source, string Status, bool Done, IReadOnlyList<QuestLogStepRow> Steps,
    bool HasCount, int Have, int Need, string Reward)
{
    public bool HasReward => Reward.Length > 0;
    public string StatusColor => Done ? "#8a8f99" : Status.StartsWith("Ready", StringComparison.Ordinal) ? "#7fd07f" : "#e8c060";
    public double CardOpacity => Done ? 0.7 : 1;
}

/// <summary>AVABand's quest log: the quests and jobs under way (each with where it stands), then those done.</summary>
public sealed class QuestLogViewModel
{
    public QuestLogViewModel(GameSession game)
    {
        Cards = [.. game.QuestLog().Select(e => new QuestLogCard(e.Name, e.Source, e.Status, e.Done,
            [.. e.Steps.Select(s => new QuestLogStepRow(s.Done ? "✓" : "•", s.Text, s.Done))],
            e.HasCount, e.Have, e.Need, e.Reward))];
        var active = Cards.Count(c => !c.Done);
        var jobs = game.AvaQuests.Board.Count(j => j.Taken);
        Summary = !game.AvaQuestsOn ? "This character was made without AVABand's quests."
            : Cards.Count == 0 ? "No quests yet. The Prancing Pony (9) in town has work, and its notice board has jobs."
            : $"{active} under way ({jobs} of {GameSession.BoardTakenMax} notice-board jobs), {Cards.Count - active} done";
    }

    public string Title => "Quest log";
    public string Summary { get; }
    public IReadOnlyList<QuestLogCard> Cards { get; }
    public bool IsEmpty => Cards.Count == 0;
}

public sealed partial class MainWindowViewModel
{
    public event Action<QuestLogViewModel>? QuestLogRequested;

    public QuestLogViewModel CreateQuestLog() => new(_game);

    /// <summary>Game → Quest log… (Ctrl+J): the quests and jobs under way, and how far along each is.</summary>
    [RelayCommand]
    public void ShowQuestLog() => QuestLogRequested?.Invoke(CreateQuestLog());
}
