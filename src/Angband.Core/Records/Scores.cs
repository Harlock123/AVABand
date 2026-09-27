using System.Text.Json;
using Angband.Core.Game;

namespace Angband.Core.Records;

public static class Scoring
{
    /// <summary>Angband's <c>total_points</c>: experience plus 100 points per dungeon level reached.</summary>
    public static long Points(Player p) => Math.Max(p.Experience, p.MaxExperience) + 100L * p.MaxDepth;
}

/// <summary>One line of the high-score table.</summary>
public sealed class ScoreEntry
{
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public int Level { get; set; }
    public int MaxLevel { get; set; }
    public int Depth { get; set; }
    public int MaxDepth { get; set; }
    public long Experience { get; set; }
    public long Gold { get; set; }
    public long Turns { get; set; }
    public long Points { get; set; }
    /// <summary>What killed the character; null while alive (or retired).</summary>
    public string? KilledBy { get; set; }
    public ulong Seed { get; set; }
    /// <summary>Defeated Morgoth.</summary>
    public bool Won { get; set; }
    public DateTime DateUtc { get; set; }

    public bool IsAlive => KilledBy is null;

    public static ScoreEntry For(GameSession game, DateTime? whenUtc = null)
    {
        var p = game.Player;
        return new ScoreEntry
        {
            Name = p.Name, Race = p.Race?.Name ?? "", Class = p.Class?.Name ?? "",
            Level = p.Level, MaxLevel = p.MaxLevel, Depth = p.Depth, MaxDepth = p.MaxDepth,
            Experience = p.Experience, Gold = p.Gold, Turns = game.NormalTurns, Points = Scoring.Points(p),
            KilledBy = p.IsDead ? p.KilledBy ?? "something" : null, Seed = game.Seed,
            DateUtc = whenUtc ?? DateTime.UtcNow,
            Won = p.IsWinner,
        };
    }
}

/// <summary>
/// The high-score table (Angband's <c>scores.raw</c>): best first, ties broken by the fewest turns, then
/// the earliest. Kept to <see cref="MaxEntries"/> entries and stored as JSON.
/// </summary>
public sealed class ScoreBoard
{
    public const int MaxEntries = 100;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly List<ScoreEntry> _entries = [];

    public IReadOnlyList<ScoreEntry> Entries => _entries;

    /// <summary>Adds a score; returns its 1-based rank, or 0 if it didn't make the table.</summary>
    public int Add(ScoreEntry entry)
    {
        var rank = RankOf(entry);
        if (rank > MaxEntries) return 0;
        _entries.Insert(rank - 1, entry);
        if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        return rank;
    }

    /// <summary>Where this score would place (1-based) without adding it — for a living character.</summary>
    public int RankOf(ScoreEntry entry)
    {
        var i = 0;
        while (i < _entries.Count && Compare(_entries[i], entry) <= 0) i++;
        return i + 1;
    }

    private static int Compare(ScoreEntry a, ScoreEntry b)
    {
        var c = b.Points.CompareTo(a.Points);
        if (c != 0) return c;
        c = a.Turns.CompareTo(b.Turns);
        return c != 0 ? c : a.DateUtc.CompareTo(b.DateUtc);
    }

    public static ScoreBoard Load(string path)
    {
        var board = new ScoreBoard();
        try
        {
            if (!File.Exists(path)) return board;
            var entries = JsonSerializer.Deserialize<List<ScoreEntry>>(File.ReadAllText(path), Json) ?? [];
            foreach (var e in entries) board.Add(e);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged score file starts a fresh table rather than stopping the game.
        }
        return board;
    }

    /// <summary>Writes atomically, so a crash never loses the table.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Json));
        File.Move(temp, path, overwrite: true);
    }
}
