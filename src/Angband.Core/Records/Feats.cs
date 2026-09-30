using System.Text.Json;
using Angband.Core.Definitions;
using Angband.Core.Game;

namespace Angband.Core.Records;

/// <summary>A feat: a milestone any character can reach, kept for good once one has.</summary>
/// <param name="Earned">Whether this character has done it (checked after each command).</param>
public sealed record FeatDef(string Id, string Name, string Description, Func<GameSession, bool> Earned);

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
        feats.Add(new("board", "Regular at the Pony", "Complete ten jobs from the notice board with one character.", g => g.AvaQuests.JobsDone >= 10));
        foreach (var quest in data.Quests)
            if (data.Monster(quest.Race) is { } boss)
                feats.Add(new($"quest_{quest.Id}", $"{boss.Name} Falls", $"Slay {boss.Name}.", g => g.KilledUniques.Contains(boss.Id)));
        feats.Add(new("win", "Victory", "Win the game.", g => g.Player.IsWinner));
        foreach (var cls in data.Classes)
            feats.Add(new($"win_{cls.Id}", $"Victory as a {cls.Name}", $"Win the game with a {cls.Name}.", g => g.Player.IsWinner && g.Player.Class?.Id == cls.Id));
        return feats;
    }

    /// <summary>Whether this game can earn feats: not a cheat's, a debug user's, the tutorial or a replay.</summary>
    public static bool Counts(GameSession game) => !game.IsCheater && !game.IsTutorial && !game.IsReplay;
}

/// <summary>The feats done so far, across every character (<c>feats.json</c> beside the scores).</summary>
public sealed class FeatBook
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public Dictionary<string, FeatRecord> Earned { get; private set; } = new(StringComparer.Ordinal);

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

    public static FeatBook Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return new FeatBook
                {
                    Earned = JsonSerializer.Deserialize<Dictionary<string, FeatRecord>>(File.ReadAllText(path), Json) is { } earned
                        ? new Dictionary<string, FeatRecord>(earned, StringComparer.Ordinal) : new(StringComparer.Ordinal),
                };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged file starts afresh rather than stopping the game.
        }
        return new FeatBook();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Earned, Json));
        File.Move(temp, path, overwrite: true);
    }
}
