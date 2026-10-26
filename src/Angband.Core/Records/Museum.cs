using System.Text.Json;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Monsters;

namespace Angband.Core.Records;

/// <summary>When a museum piece came in, who brought it, and how deep.</summary>
public sealed class MuseumEntry
{
    public DateTime WhenUtc { get; set; }
    /// <summary>"Bob the Elf Mage"; empty for what was known before the museum opened.</summary>
    public string Character { get; set; } = "";
    public int Depth { get; set; }
    /// <summary>For a unique: whether one of your characters has claimed a bounty on it (false in older museums).</summary>
    public bool Bounty { get; set; }
}

/// <summary>
/// The museum (AVABand's own, <c>museum.json</c> beside the scores): across every character, each
/// standard artifact found, each unique slain (and whether for a bounty), and each kind of monster seen — and who did it first.
/// Like the feats, a cheat's, a debug user's, the tutorial's and a replay's finds don't count; a game
/// with random artifacts adds no artifacts (they aren't the standard set).
/// </summary>
public sealed class Museum
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public Dictionary<string, MuseumEntry> Artifacts { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, MuseumEntry> Uniques { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, MuseumEntry> Monsters { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Whether what was known before there was a museum has been filled in (once).</summary>
    public bool Backfilled { get; set; }

    /// <summary>The kinds of monster the museum counts: the game's own races and the seasonal ones (not ghosts or hirelings).</summary>
    public static IEnumerable<MonsterRaceDef> Races(GameData data) => data.AllMonsters.Where(r => !r.Has(MonsterFlags.Questor) || r.IsUnique);

    /// <summary>How many uniques' places are marked "Bounty claimed".</summary>
    public int CountBounties() => Uniques.Values.Count(e => e.Bounty);

    /// <summary>The uniques it counts.</summary>
    public static IEnumerable<MonsterRaceDef> UniqueRaces(GameData data) => Races(data).Where(r => r.IsUnique);

    /// <summary>
    /// Once: the museum's first pieces from what came before it — every monster in the shared monster
    /// memory seen (and each unique there killed), and the artifacts the headstones' characters wore.
    /// </summary>
    public void Backfill(GameData data, MonsterLoreBook lore, Graveyard yard)
    {
        var races = Races(data).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, l) in lore.Races)
        {
            if (!races.Contains(id)) continue;
            if (l.Sights > 0 || l.TotalKills > 0) Monsters.TryAdd(id, new MuseumEntry { WhenUtc = DateTime.UtcNow });
            if (l.TotalKills > 0 && data.Monster(id)?.IsUnique == true) Uniques.TryAdd(id, new MuseumEntry { WhenUtc = DateTime.UtcNow });
        }
        foreach (var fallen in yard.Fallen.Where(f => !f.Randarts).OrderBy(f => f.DateUtc))
            foreach (var json in fallen.Equipment ?? [])
                if (ArtifactIn(json) is { } art && data.Artifacts.Any(a => a.Id == art))
                    Artifacts.TryAdd(art, new MuseumEntry { WhenUtc = fallen.DateUtc, Character = $"{fallen.Name} the {fallen.Race} {fallen.Class}", Depth = fallen.MaxDepth });
        Backfilled = true;
    }

    /// <summary>The artifact a saved item is (<c>SaveGame.ItemToJson</c>), if it is one.</summary>
    private static string? ArtifactIn(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("Artifact", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>What this game has newly found for the museum, recorded; the artifacts and uniques among them returned by name (for the messages).</summary>
    public IReadOnlyList<string> Check(GameSession game, DateTime? whenUtc = null)
    {
        if (!Feats.Counts(game)) return [];
        var data = game.Data;
        var who = $"{game.Player.Name} the {game.Player.Race?.Name} {game.Player.Class?.Name}".Replace("  ", " ");
        MuseumEntry Entry() => new() { WhenUtc = whenUtc ?? DateTime.UtcNow, Character = who, Depth = game.Player.Depth };
        var news = new List<string>();
        if (game.RandartSeed is null)
            foreach (var id in game.Knowledge.SeenArtifacts)
                if (!Artifacts.ContainsKey(id) && data.Artifacts.FirstOrDefault(a => a.Id == id) is { } art)
                {
                    Artifacts[id] = Entry();
                    news.Add(art.Name);
                }
        foreach (var id in game.KilledUniques)
            if (!Uniques.ContainsKey(id) && data.Monster(id) is { IsUnique: true } race)
            {
                Uniques[id] = Entry();
                news.Add(race.Name);
            }
        // A bounty claimed on a unique marks its place, whoever slew it first.
        foreach (var id in game.AvaQuests.BountiesClaimed)
            if (Uniques.TryGetValue(id, out var slain)) slain.Bounty = true;
        foreach (var (id, l) in game.Lore.Races)
            if ((l.Sights > 0 || l.TotalKills > 0) && !Monsters.ContainsKey(id) && data.Monster(id) is { } race && (!race.Has(MonsterFlags.Questor) || race.IsUnique))
                Monsters[id] = Entry();
        return news;
    }

    public static Museum Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Museum>(File.ReadAllText(path), Json) is { } museum)
            {
                museum.Artifacts = new(museum.Artifacts, StringComparer.Ordinal);
                museum.Uniques = new(museum.Uniques, StringComparer.Ordinal);
                museum.Monsters = new(museum.Monsters, StringComparer.Ordinal);
                return museum;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A damaged file starts afresh rather than stopping the game.
        }
        return new Museum();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }
}
