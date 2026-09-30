using System.Windows.Input;
using Angband.Core.Game;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>A quest on Debug → Try a quest.</summary>
public sealed record QuestTrialItem(string Name, ICommand Command);

/// <summary>Debug → Try a quest: a new debug character of your race and class, kitted out, on the quest's level.</summary>
public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<QuestTrialItem> QuestTrials => field ??=
        [.. _data.AvaQuests.Where(q => GameSession.QuestTrialLevels.ContainsKey(q.Id))
            .Select(q => new QuestTrialItem(q.Name, new RelayCommand(() => TryQuest(q.Id))))];

    public void TryQuest(string questId)
    {
        if (_data.AvaQuests.FirstOrDefault(q => q.Id == questId) is not { } quest) return;
        var level = GameSession.QuestTrialLevels[questId];
        var race = _game.Player.Race?.Id ?? "human";
        var cls = _game.Player.Class?.Id ?? _settings.LastClass;
        AskFirst($"Start a new debug character — a level-{level} {_game.Player.Race?.Name} {_game.Player.Class?.Name}, kitted out — "
                 + $"to try {quest.Name}? This game is saved first; the new one won't be scored.", () =>
        {
            StartGame((ulong)Environment.TickCount64, CharacterSpec.Default(race, cls));
            Execute(new DebugTryQuestCommand(questId));
            OnPropertyChanged(nameof(DebugMarked));
        });
        Refresh();
    }
}
