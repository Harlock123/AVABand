using System.Collections.ObjectModel;
using Angband.Core.Game;
using Angband.Core.Magic;
using Angband.Core.Records;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>A line of the daily table.</summary>
public sealed record DailyRow(DailyEntry Entry, bool IsToday)
{
    public string Day => Entry.Day;
    public string Attempt => $"try {Entry.Attempt}";
    public string Who => $"{Entry.Name} the {Entry.Race} {Entry.Class}";
    public string Reached => $"{Entry.MaxDepth * 50} ft, level {Entry.Level}";
    public string Points => Entry.Points.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
    public string Fate => Entry.Fate;
    public bool HasReplay => Entry.ReplayPath is { } path && File.Exists(path);
}

/// <summary>Game → Daily dungeon: today's character, a way in, and every try so far.</summary>
public sealed partial class DailyViewModel : ObservableObject
{
    public DailyViewModel(string today, string character, string stats, IEnumerable<DailyEntry> tries, string? replayFolder)
    {
        Today = today;
        Character = character;
        Stats = stats;
        foreach (var t in tries) Rows.Add(new DailyRow(t, t.Day == today));
        // Today's best try (the table is best first within a day) is ready to share.
        _selected = Rows.FirstOrDefault(r => r.IsToday);
        ReplayNote = replayFolder is null ? "Every try is recorded as a replay."
            : $"Every try is recorded as a replay (in {replayFolder}). Send one to a friend, or watch theirs with Game → Watch a replay → Open a replay file…";
    }

    public string Today { get; }
    public string Character { get; }
    public string Stats { get; }
    public string ReplayNote { get; }
    public ObservableCollection<DailyRow> Rows { get; } = [];
    public bool IsEmpty => Rows.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyResultCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveReplayCommand))]
    private DailyRow? _selected;

    /// <summary>Raised with a line to put on the clipboard.</summary>
    public event Action<string>? CopyRequested;

    /// <summary>Raised with a try's replay and the name to save a copy under.</summary>
    public event Action<string, string>? SaveReplayRequested;

    /// <summary>What was last copied (for tests, and said under the table).</summary>
    [ObservableProperty]
    private string _shareNote = "";

    private bool HasSelection => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyResult()
    {
        if (Selected is null) return;
        var line = Selected.Entry.ShareLine();
        CopyRequested?.Invoke(line);
        ShareNote = "Copied: " + line;
    }

    [RelayCommand(CanExecute = nameof(CanWatch))]
    private void SaveReplay()
    {
        if (Selected?.Entry.ReplayPath is { } path)
            SaveReplayRequested?.Invoke(path, Selected.Entry.ReplayFileName(Angband.Core.Persistence.ReplayFile.Extension));
    }

    /// <summary>Raised to play today's dungeon (a new try).</summary>
    public event Action? PlayRequested;

    /// <summary>Raised with a try's replay to watch.</summary>
    public event Action<string>? WatchRequested;

    [RelayCommand]
    private void Play() => PlayRequested?.Invoke();

    private bool CanWatch => Selected?.HasReplay == true;

    [RelayCommand(CanExecute = nameof(CanWatch))]
    private void Watch()
    {
        if (Selected?.Entry.ReplayPath is { } path) WatchRequested?.Invoke(path);
    }
}

public sealed partial class MainWindowViewModel
{
    /// <summary>Raised to show the daily dungeon's window.</summary>
    public event Action<DailyViewModel>? DailyRequested;

    [RelayCommand]
    public void ShowDaily() => DailyRequested?.Invoke(CreateDaily());

    public DailyViewModel CreateDaily()
    {
        var day = DailyDungeon.Today();
        var spec = DailyDungeon.SpecFor(_data, day, DailyName);
        var race = _data.Race(spec.RaceId);
        var cls = _data.Class(spec.ClassId);
        var stats = string.Join("  ", CharacterSpec.StatIds.Select(s =>
            $"{s.ToUpperInvariant()} {StatTables.Format(Birth.FinalStat(spec.BaseStats[s], race, cls, s))}"));
        var daily = new DailyViewModel(DailyDungeon.Stamp(day), $"Today: a {race?.Name} {cls?.Name}", stats,
            _records?.LoadDaily().Ordered() ?? [], ReplayDirectory);
        daily.PlayRequested += StartDaily;
        daily.WatchRequested += path => PlayReplay(path);
        return daily;
    }

    /// <summary>The name a daily character goes by: the last character's.</summary>
    private string DailyName => _settings.LastCharacter?.Name ?? "Adventurer";

    /// <summary>A new try at today's dungeon: the day's seed and character (not remembered as the last character).</summary>
    [RelayCommand]
    public void StartDaily()
    {
        var day = DailyDungeon.Today();
        var spec = DailyDungeon.SpecFor(_data, day, DailyName);
        StartGame(DailyDungeon.SeedFor(day), spec, game => game.Player.DailyDate = DailyDungeon.Stamp(day));
        AddMessage($"Today's daily dungeon ({DailyDungeon.Stamp(day)}): the same for everyone who plays it today. Good luck.");
    }

    /// <summary>On death: a daily try goes in the daily table (a cheat's doesn't).</summary>
    private void RecordDailyTry(ScoreEntry entry)
    {
        if (_records is null || _game.Player.DailyDate is not { } day || _game.IsCheater) return;
        var fate = entry.Won ? "retired victorious"
            : $"killed by {entry.KilledBy}{(entry.Depth == 0 ? " in the town" : $" at {entry.Depth * 50} ft")}";
        if (_records.RecordDaily(new DailyEntry
            {
                Day = day, Name = entry.Name, Race = entry.Race, Class = entry.Class, Level = entry.MaxLevel, MaxDepth = entry.MaxDepth,
                Points = entry.Points, Turns = entry.Turns, Fate = fate, DateUtc = entry.DateUtc, ReplayPath = CurrentReplayPath,
            }) is { } done)
        {
            var board = _records.LoadDaily();
            var best = board.For(day).First();
            foreach (var feat in _featBook.CheckBoard(board, FeatDefs)) AddMessage($"Feat: {feat.Name}! ({feat.Description})");
            _records.SaveFeats(_featBook);
            AddMessage($"Daily dungeon, {day}: try {done.Attempt} reached {done.MaxDepth * 50} ft"
                       + (ReferenceEquals(best, done) || best.Attempt == done.Attempt ? " — your best today." : $" (your best today: {best.MaxDepth * 50} ft)."));
        }
    }
}
