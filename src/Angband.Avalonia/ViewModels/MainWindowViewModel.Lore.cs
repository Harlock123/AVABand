using System.Collections.ObjectModel;
using Angband.Core.Monsters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

// Monster memory in the UI: recall (from look/target mode, see MainWindowViewModel.Targeting.cs) and
// the Monsters tab of the '~' knowledge browser (KnowledgeViewModels.cs).
public sealed partial class MainWindowViewModel
{
    private MonsterLoreBook _lore = new();

    private void UseLore(MonsterLoreBook lore)
    {
        _lore = lore;
        _game.Lore = lore;
    }

    private void SaveLore() => _records?.SaveLore(_lore);

    /// <summary>Opens the recall text for a race (in the text window the character sheet uses).</summary>
    public void ShowRecall(Angband.Core.Definitions.MonsterRaceDef race) =>
        CharacterSheetRequested?.Invoke(new CharacterSheetViewModel($"Monster recall: {race.Name}", _game.Recall(race), null)
        {
            Runs = ColoredText.FromMarked(_game.RecallMarked(race), _cells),
        });

    /// <summary>~: the knowledge browser (monsters met, objects seen, runes learned, egos and artifacts found).</summary>
    [RelayCommand]
    public void ShowKnowledge() => KnowledgeRequested?.Invoke(CreateKnowledge());

    /// <summary>'?' or F1: the list of commands and the keys bound to them.</summary>
    public event Action<KeyCommandsViewModel>? KeyCommandsRequested;

    [RelayCommand]
    public void ShowKeyCommands() => KeyCommandsRequested?.Invoke(new KeyCommandsViewModel(Bindings));

    /// <summary>'?': the help pages.</summary>
    public event Action<HelpViewModel>? HelpRequested;

    [RelayCommand]
    public void ShowHelp() => HelpRequested?.Invoke(new HelpViewModel());

    /// <summary>Ctrl+P: the message history.</summary>
    public event Action<MessageHistoryViewModel>? MessageHistoryRequested;

    /// <summary>Raised to show the journey window.</summary>
    public event Action<JourneyViewModel>? JourneyRequested;

    /// <summary>Game → Your journey…: depth over time, each depth's doings, and the history.</summary>
    [RelayCommand]
    public void ShowJourney() => JourneyRequested?.Invoke(CreateJourney());

    public JourneyViewModel CreateJourney() => new(_game, HistoryText());

    [RelayCommand]
    public void ShowMessageHistory() => MessageHistoryRequested?.Invoke(new MessageHistoryViewModel([.. _history]));

    /// <param name="symbol">Only the monsters shown with this symbol ('/' then "Recall details?").</param>
    public MonsterKnowledgeViewModel CreateMonsterKnowledge(char? symbol = null)
    {
        var rows = _data.Monsters
            .Where(r => _lore.Find(r.Id) is { } l && (l.Sights > 0 || l.TotalKills > 0 || l.Deaths > 0))
            .Where(r => symbol is null || r.Glyph == symbol)
            .OrderBy(r => r.Depth).ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new MonsterKnowledgeRow(r, r.Glyph.ToString(), _cells.Color(r.Color),
                _game.CharacterKills.GetValueOrDefault(r.Id), _lore.Find(r.Id)!.TotalKills))
            .ToList();
        return new MonsterKnowledgeViewModel(rows, _game.Recall, race => ColoredText.FromMarked(_game.RecallMarked(race), _cells));
    }
}

/// <summary>One line of the monster knowledge list.</summary>
public sealed record MonsterKnowledgeRow(Angband.Core.Definitions.MonsterRaceDef Race, string Glyph, uint GlyphColor, int Kills, int TotalKills)
{
    public global::Avalonia.Media.IBrush GlyphBrush { get; } =
        new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(global::Avalonia.Media.Color.FromUInt32(GlyphColor));
    public string Name => Race.Name;
    public string KillsText => TotalKills == 0 ? "" : Kills == TotalKills ? $"{Kills} slain" : $"{Kills} slain ({TotalKills} in all)";
}

/// <summary>The monster knowledge browser: pick a monster to read its recall.</summary>
public sealed partial class MonsterKnowledgeViewModel : ObservableObject
{
    private readonly Func<Angband.Core.Definitions.MonsterRaceDef, string> _recall;
    private readonly Func<Angband.Core.Definitions.MonsterRaceDef, IReadOnlyList<ColoredRun>>? _runs;

    public MonsterKnowledgeViewModel(IReadOnlyList<MonsterKnowledgeRow> rows, Func<Angband.Core.Definitions.MonsterRaceDef, string> recall,
        Func<Angband.Core.Definitions.MonsterRaceDef, IReadOnlyList<ColoredRun>>? runs = null)
    {
        _recall = recall;
        _runs = runs;
        foreach (var row in rows) Rows.Add(row);
        Selected = Rows.FirstOrDefault();
    }

    public ObservableCollection<MonsterKnowledgeRow> Rows { get; } = [];
    public bool IsEmpty => Rows.Count == 0;
    public string Summary => Rows.Count == 1 ? "1 kind of monster encountered" : $"{Rows.Count} kinds of monster encountered";

    [ObservableProperty] private MonsterKnowledgeRow? _selected;
    [ObservableProperty] private string _recallText = "";
    /// <summary>The recall in Angband's colours (spells by how dangerous they are to you).</summary>
    [ObservableProperty] private IReadOnlyList<ColoredRun>? _recallRuns;

    partial void OnSelectedChanged(MonsterKnowledgeRow? value)
    {
        RecallText = value is null ? "" : _recall(value.Race);
        RecallRuns = value is null || _runs is null ? null : _runs(value.Race);
    }
}
