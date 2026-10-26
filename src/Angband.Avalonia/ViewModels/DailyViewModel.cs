using System.Collections.ObjectModel;
using Angband.Core.Game;
using Angband.Core.Magic;
using Angband.Core.Records;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>A line of the daily (or weekly) table.</summary>
public sealed record DailyRow(DailyEntry Entry, bool IsToday)
{
    public string Day => Entry.Day;
    public string Attempt => $"try {Entry.Attempt}";
    public string Who => $"{Entry.Name} the {Entry.Race} {Entry.Class}{Angband.Core.Records.Scoring.ModdedTag(Entry.Mods)}";
    public string Reached => $"{Entry.MaxDepth * 50} ft, level {Entry.Level}";
    public string Points => Entry.Points.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
    public string Fate => Entry.Fate;
    public bool HasReplay => Entry.ReplayPath is { } path && File.Exists(path);
}

/// <summary>
/// Game → Daily dungeon: today's character, a way in, and every try so far. The weekly challenge
/// (Game → Weekly challenge) shows in the same window: this week's character and its twist.
/// </summary>
public sealed partial class DailyViewModel : ObservableObject
{
    /// <param name="today">The day ("2026-10-26"), or for the weekly challenge the week ("2026-W44").</param>
    /// <param name="weeklyTwist">The week's twist: this is the weekly challenge, not the daily dungeon.</param>
    public DailyViewModel(string today, string character, string stats, IEnumerable<DailyEntry> tries, string? replayFolder,
        WeeklyTwist? weeklyTwist = null)
    {
        Today = today;
        Character = character;
        Stats = stats;
        IsWeekly = weeklyTwist is not null;
        TwistLine = weeklyTwist is null ? "" : $"This week's twist — {weeklyTwist.Name}: {weeklyTwist.Description}";
        foreach (var t in tries) Rows.Add(new DailyRow(t, t.Day == today));
        // Today's (this week's) best try (the table is best first within a day) is ready to share.
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

    /// <summary>The weekly challenge (rather than the daily dungeon).</summary>
    public bool IsWeekly { get; }

    /// <summary>The week's twist, said under its character ("" for the daily dungeon).</summary>
    public string TwistLine { get; }

    public string WindowTitle => IsWeekly ? "The weekly challenge" : "The daily dungeon";
    public string Heading => $"{WindowTitle} · {Today}";
    public string PlayLabel => IsWeekly ? "Play this week's challenge" : "Play today's dungeon";
    public string PeriodHeader => IsWeekly ? "Week" : "Day";

    public string Blurb => IsWeekly
        ? "The same dungeon, character and twist for everyone who plays it this week. Try as often as you like until Sunday is out (UTC); a new challenge on Monday."
        : "The same dungeon and character for everyone who plays it today; a new one tomorrow (UTC).";

    public string EmptyText => IsWeekly ? "No tries yet. Every week brings a new challenge, with a new twist." : "No tries yet. Every day brings a new dungeon.";

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
        // A weekly try says how many tries its week has had.
        var line = Selected.Entry is WeeklyEntry week ? week.ShareLine(Rows.Count(r => r.Entry.Day == week.Day)) : Selected.Entry.ShareLine();
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
    /// <summary>Raised to show the daily dungeon's window (the weekly challenge's too).</summary>
    public event Action<DailyViewModel>? DailyRequested;

    [RelayCommand]
    public void ShowDaily() => DailyRequested?.Invoke(CreateDaily());

    public DailyViewModel CreateDaily()
    {
        var day = DailyDungeon.Today();
        var spec = DailyDungeon.SpecFor(_data, day, DailyName);
        var daily = new DailyViewModel(DailyDungeon.Stamp(day), $"Today: a {SpecName(spec)}", SpecStats(spec),
            _records?.LoadDaily().Ordered() ?? [], ReplayDirectory);
        daily.PlayRequested += StartDaily;
        daily.WatchRequested += path => PlayReplay(path);
        return daily;
    }

    /// <summary>A seeded run's character, as the window names it ("Dwarf Priest").</summary>
    private string SpecName(CharacterSpec spec) => $"{_data.Race(spec.RaceId)?.Name} {_data.Class(spec.ClassId)?.Name}";

    /// <summary>A seeded run's character's stats, as they'll be (race and class bonuses on).</summary>
    private string SpecStats(CharacterSpec spec)
    {
        var race = _data.Race(spec.RaceId);
        var cls = _data.Class(spec.ClassId);
        return string.Join("  ", CharacterSpec.StatIds.Select(s =>
            $"{s.ToUpperInvariant()} {StatTables.Format(Birth.FinalStat(spec.BaseStats[s], race, cls, s))}"));
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

    /// <summary>Game → Weekly challenge: this week's character and twist, in the daily dungeon's window.</summary>
    [RelayCommand]
    public void ShowWeekly() => DailyRequested?.Invoke(CreateWeekly());

    public DailyViewModel CreateWeekly()
    {
        var week = WeeklyChallenge.ThisWeek();
        var spec = WeeklyChallenge.SpecFor(_data, week, DailyName);
        var weekly = new DailyViewModel(week, $"This week: a {SpecName(spec)}", SpecStats(spec),
            _records?.LoadWeekly().Ordered() ?? [], ReplayDirectory, WeeklyChallenge.TwistFor(week));
        weekly.PlayRequested += StartWeekly;
        weekly.WatchRequested += path => PlayReplay(path);
        return weekly;
    }

    /// <summary>A new try at this week's challenge: the week's seed, character and twist (not remembered as the last character).</summary>
    [RelayCommand]
    public void StartWeekly()
    {
        var week = WeeklyChallenge.ThisWeek();
        var spec = WeeklyChallenge.SpecFor(_data, week, DailyName);
        var twist = WeeklyChallenge.TwistFor(week);
        StartGame(WeeklyChallenge.SeedFor(week), spec, game => WeeklyChallenge.Prepare(game, week));
        AddMessage($"This week's challenge ({week}), the same for everyone who plays it this week. The twist — {twist.Name}: {twist.Description} Good luck.");
    }

    /// <summary>How a try ended, for the daily and weekly tables.</summary>
    private static string TryFate(ScoreEntry entry) => entry.Won ? "retired victorious"
        : $"killed by {entry.KilledBy}{(entry.Depth == 0 ? " in the town" : $" at {entry.Depth * 50} ft")}";

    /// <summary>On death: a daily try goes in the daily table (a cheat's doesn't).</summary>
    private void RecordDailyTry(ScoreEntry entry)
    {
        if (_records is null || _game.Player.DailyDate is not { } day || _game.IsCheater) return;
        if (_records.RecordDaily(new DailyEntry
            {
                Day = day, Name = entry.Name, Race = entry.Race, Class = entry.Class, Level = entry.MaxLevel, MaxDepth = entry.MaxDepth,
                Points = entry.Points, Turns = entry.Turns, Fate = TryFate(entry), DateUtc = entry.DateUtc, ReplayPath = CurrentReplayPath,
                Mods = entry.Mods,
            }) is { } done)
        {
            var board = _records.LoadDaily();
            var best = board.For(day).First();
            AnnounceFeats(_featBook.CheckBoard(board, FeatDefs));
            _records.SaveFeats(_featBook);
            AddMessage($"Daily dungeon, {day}: try {done.Attempt} reached {done.MaxDepth * 50} ft"
                       + (ReferenceEquals(best, done) || best.Attempt == done.Attempt ? " — your best today." : $" (your best today: {best.MaxDepth * 50} ft)."));
        }
    }

    /// <summary>On death: a weekly try goes in the weekly table (a cheat's doesn't).</summary>
    private void RecordWeeklyTry(ScoreEntry entry)
    {
        if (_records is null || _game.Player.WeeklyWeek is not { } week || _game.IsCheater) return;
        if (_records.RecordWeekly(new WeeklyEntry
            {
                Day = week, Twist = _game.Twist?.Name ?? "", Name = entry.Name, Race = entry.Race, Class = entry.Class, Level = entry.MaxLevel,
                MaxDepth = entry.MaxDepth, Points = entry.Points, Turns = entry.Turns, Fate = TryFate(entry), DateUtc = entry.DateUtc,
                ReplayPath = CurrentReplayPath,
            }) is { } done)
        {
            var board = _records.LoadWeekly();
            var best = board.For(week).First();
            foreach (var feat in _featBook.CheckWeeklyBoard(board, FeatDefs)) AddMessage($"Feat: {feat.Name}! ({feat.Description})");
            _records.SaveFeats(_featBook);
            AddMessage($"Weekly challenge, {week}: try {done.Attempt} reached {done.MaxDepth * 50} ft"
                       + (best.Attempt == done.Attempt ? " — your best this week." : $" (your best this week: {best.MaxDepth * 50} ft)."));
        }
    }
}
