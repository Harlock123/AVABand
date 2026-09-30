using System.Text.Json;
using Angband.Core.Definitions;
using Angband.Core.Game;

namespace Angband.Core.Records;

/// <summary>A feat: a milestone any character can reach, kept for good once one has.</summary>
/// <param name="Earned">Whether this character has done it (checked after each command).</param>
/// <param name="OnBoard">For a daily-dungeon feat: whether the daily board shows it done (checked as each try is recorded).</param>
public sealed record FeatDef(string Id, string Name, string Description, Func<GameSession, bool> Earned, Func<DailyBoard, bool>? OnBoard = null);

/// <summary>When a feat was first done, and by whom.</summary>
public sealed class FeatRecord
{
    public DateTime WhenUtc { get; set; }
    public string Character { get; set; } = "";
}

/// <summary>
/// AVABand's feats (the Knowledge screen's Feats page): milestones across every character you play —
/// depths reached, levels, uniques slain, quests done, and a win with each class. A character that
/// cheated or used a debug command, the tutorial and replays earn none.
/// </summary>
public static class Feats
{
    /// <summary>Every feat, in the order the page lists them (the class wins from the game's classes).</summary>
    public static IReadOnlyList<FeatDef> All(GameData data)
    {
        var feats = new List<FeatDef>();
        foreach (var (depth, name) in new[] { (10, "Below the Town"), (20, "Into the Deep"), (40, "The Lower Halls"), (60, "Where the Light Fails"),
                     (80, "The Pits of Angband"), (100, "The Iron Throne") })
            feats.Add(new($"depth_{depth}", name, $"Reach {depth * data.Constants.FeetPerLevel} ft.", g => g.Player.MaxDepth >= depth));
        foreach (var (level, name) in new[] { (10, "Seasoned"), (20, "Veteran"), (30, "Champion"), (40, "Hero of the West"), (50, "Legend") })
            feats.Add(new($"level_{level}", name, $"Reach character level {level}.", g => g.Player.MaxLevel >= level));
        foreach (var (count, name) in new[] { (1, "A Name Unmade"), (10, "Bane of the Named"), (50, "Scourge of Legends") })
            feats.Add(new($"uniques_{count}", name, count == 1 ? "Slay a unique monster." : $"Slay {count} unique monsters with one character.",
                g => g.KilledUniques.Count >= count));
        feats.Add(new("gold", "Dragon's Hoard", "Carry 100,000 gold.", g => g.Player.Gold >= 100_000));
        feats.Add(new("artifact", "Bearer of Renown", "Wield or wear an artifact.", g => g.Player.Inventory.Equipped.Any(i => i.IsArtifact)));
        feats.Add(new("ava_quest", "Friend of Bree", "Bring one of the Prancing Pony's quests to a good end.",
            g => g.AvaQuests.Quests.Values.Any(q => q.IsDone && q.Stage != "lost")));
        feats.Add(new("ava_quests_all", "Every Trouble Mended", "Bring every one of the Prancing Pony's story quests to a good end, with one character.",
            g => data.AvaQuests.All(q => g.AvaQuests.Get(q.Id) is { IsDone: true } s && s.Stage != "lost")));
        feats.Add(new("heart_returned", "Friend of Belegost", "Give the Heart of the Mountain back to the dwarves.",
            g => g.AvaQuests.Get("heart")?.Stage == "returned"));
        feats.Add(new("watch_held", "The Horns of the West", $"Hold the watchtower all {GameSession.WatchTurns} turns, until the horns answer.",
            g => g.AvaQuests.Get("watch") is { Stage: "relieved" } w && w.N("held") >= GameSession.WatchTurns));
        feats.Add(new("stone_kept", "The Eye Looks Back", "Keep the palantír, and look into it ten times.",
            g => g.AvaQuests.Get("stone") is { Stage: "keep" } s && s.N("looks") >= 10));
        feats.Add(new("board", "Regular at the Pony", "Complete ten jobs from the notice board with one character.", g => g.AvaQuests.JobsDone >= 10));
        foreach (var quest in data.Quests)
            if (data.Monster(quest.Race) is { } boss)
                feats.Add(new($"quest_{quest.Id}", $"{boss.Name} Falls", $"Slay {boss.Name}.", g => g.KilledUniques.Contains(boss.Id)));
        feats.Add(new("daily", "Daily Delver", "Finish a try at the daily dungeon.", _ => false, b => b.Entries.Count > 0));
        feats.Add(new("daily_deep", "Deep of the Day", $"Reach {20 * data.Constants.FeetPerLevel} ft in a daily dungeon.",
            g => g.Player.DailyDate is not null && g.Player.MaxDepth >= 20));
        feats.Add(new("daily_week", "Seven Days Running", "Try the daily dungeon seven days in a row.", _ => false, b => DailyStreak(b) >= 7));
        feats.Add(new("win", "Victory", "Win the game.", g => g.Player.IsWinner));
        foreach (var cls in data.Classes)
            feats.Add(new($"win_{cls.Id}", $"Victory as a {cls.Name}", $"Win the game with a {cls.Name}.", g => g.Player.IsWinner && g.Player.Class?.Id == cls.Id));
        return feats;
    }

    /// <summary>The longest run of days in a row with a daily-dungeon try on the board.</summary>
    public static int DailyStreak(DailyBoard board)
    {
        var days = board.Entries.Select(e => DateOnly.TryParseExact(e.Day, "yyyy-MM-dd", out var d) ? d : (DateOnly?)null)
            .OfType<DateOnly>().Distinct().Order().ToList();
        var best = 0;
        for (int i = 0, run = 0; i < days.Count; i++)
        {
            run = i > 0 && days[i].DayNumber == days[i - 1].DayNumber + 1 ? run + 1 : 1;
            best = Math.Max(best, run);
        }
        return best;
    }

    /// <summary>Whether this game can earn feats: not a cheat's, a debug user's, the tutorial or a replay.</summary>
    public static bool Counts(GameSession game) => !game.IsCheater && !game.IsTutorial && !game.IsReplay;
}

/// <summary>The feats done so far, across every character (<c>feats.json</c> beside the scores).</summary>
public sealed class FeatBook
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public Dictionary<string, FeatRecord> Earned { get; private set; } = new(StringComparer.Ordinal);

    /// <summary>Whether the feats characters did before there were feats have been filled in (once).</summary>
    public bool Backfilled { get; set; }

    /// <summary>What <c>feats.json</c> holds.</summary>
    private sealed class FeatFile
    {
        public bool Backfilled { get; set; }
        public Dictionary<string, FeatRecord> Earned { get; set; } = [];
    }

    /// <summary>
    /// The feats characters did before there were feats, from the high-score table (which no cheat
    /// ever enters): the depths reached, the levels, and wins (with the class). Oldest first, so the
    /// first to do each is the one named. Done once; returns the feats filled in.
    /// </summary>
    public IReadOnlyList<FeatDef> Backfill(IEnumerable<ScoreEntry> entries, IReadOnlyList<FeatDef> feats, GameData data)
    {
        var added = new List<FeatDef>();
        foreach (var e in entries.OrderBy(e => e.DateUtc))
        {
            var classId = data.Classes.FirstOrDefault(c => c.Name == e.Class)?.Id;
            foreach (var feat in feats.Where(f => !Earned.ContainsKey(f.Id)))
            {
                var parts = feat.Id.Split('_', 2);
                var done = parts[0] switch
                {
                    "depth" => int.TryParse(parts[1], out var d) && e.MaxDepth >= d,
                    "level" => int.TryParse(parts[1], out var l) && Math.Max(e.Level, e.MaxLevel) >= l,
                    "win" => e.Won && (parts.Length == 1 || parts[1] == classId),
                    _ => false,
                };
                if (!done) continue;
                Earned[feat.Id] = new FeatRecord { WhenUtc = e.DateUtc, Character = $"{e.Name} the {e.Race} {e.Class}" };
                added.Add(feat);
            }
        }
        Backfilled = true;
        return added;
    }

    /// <summary>
    /// Records the feats this game has newly done (if it counts), and returns them — for the
    /// messages, and so the caller knows to save.
    /// </summary>
    public IReadOnlyList<FeatDef> Check(GameSession game, IReadOnlyList<FeatDef> feats, DateTime? whenUtc = null)
    {
        if (!Feats.Counts(game)) return [];
        var fresh = feats.Where(f => !Earned.ContainsKey(f.Id) && f.Earned(game)).ToList();
        var who = $"{game.Player.Name} the {game.Player.Race?.Name} {game.Player.Class?.Name}".Replace("  ", " ");
        foreach (var feat in fresh) Earned[feat.Id] = new FeatRecord { WhenUtc = whenUtc ?? DateTime.UtcNow, Character = who };
        return fresh;
    }

    /// <summary>Records the daily-dungeon feats the board newly shows (named for its latest try); returns them.</summary>
    public IReadOnlyList<FeatDef> CheckBoard(DailyBoard board, IReadOnlyList<FeatDef> feats, DateTime? whenUtc = null)
    {
        var latest = board.Entries.OrderBy(e => e.DateUtc).LastOrDefault();
        if (latest is null) return [];
        var fresh = feats.Where(f => f.OnBoard is { } done && !Earned.ContainsKey(f.Id) && done(board)).ToList();
        foreach (var feat in fresh)
            Earned[feat.Id] = new FeatRecord { WhenUtc = whenUtc ?? latest.DateUtc, Character = $"{latest.Name} the {latest.Race} {latest.Class}" };
        return fresh;
    }

    public static FeatBook Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(text);
                // (The first feats.json was the earned feats alone; now it says whether it was backfilled too.)
                if (doc.RootElement.TryGetProperty("Earned", out _) && JsonSerializer.Deserialize<FeatFile>(text, Json) is { } file)
                    return new FeatBook { Earned = new Dictionary<string, FeatRecord>(file.Earned, StringComparer.Ordinal), Backfilled = file.Backfilled };
                return new FeatBook
                {
                    Earned = JsonSerializer.Deserialize<Dictionary<string, FeatRecord>>(text, Json) is { } earned
                        ? new Dictionary<string, FeatRecord>(earned, StringComparer.Ordinal) : new(StringComparer.Ordinal),
                };
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A damaged file starts afresh rather than stopping the game.
        }
        return new FeatBook();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FeatFile { Backfilled = Backfilled, Earned = Earned }, Json));
        File.Move(temp, path, overwrite: true);
    }
}
