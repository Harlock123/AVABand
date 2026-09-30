using System.Text.Json;

namespace Angband.Core.Records;

/// <summary>One fallen (or retired) character: how it ended, where, and its epitaph.</summary>
public sealed class FallenRecord
{
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public int Level { get; set; }
    public int MaxDepth { get; set; }
    /// <summary>Where it ended.</summary>
    public int Depth { get; set; }
    /// <summary>What killed it; for a winner who retired, "retired victorious".</summary>
    public string KilledBy { get; set; } = "";
    public bool Won { get; set; }
    public bool Heroic { get; set; }
    public long Points { get; set; }
    public long Turns { get; set; }
    public ulong Seed { get; set; }
    public DateTime DateUtc { get; set; }
    public string Epitaph { get; set; } = "";
    /// <summary>The character dump written at its end, if there is one.</summary>
    public string? DumpPath { get; set; }

    /// <summary>The replay of its whole game, if one was recorded ("watch their last moments").</summary>
    public string? ReplayPath { get; set; }

    public static FallenRecord From(ScoreEntry e, string? dumpPath = null, string? replayPath = null)
    {
        var record = new FallenRecord
        {
            Name = e.Name, Race = e.Race, Class = e.Class, Level = e.MaxLevel > 0 ? e.MaxLevel : e.Level, MaxDepth = e.MaxDepth,
            Depth = e.Depth, KilledBy = e.Won ? "retired victorious" : e.KilledBy ?? "something", Won = e.Won, Heroic = e.Heroic,
            Points = e.Points, Turns = e.Turns, Seed = e.Seed, DateUtc = e.DateUtc, DumpPath = dumpPath, ReplayPath = replayPath,
        };
        record.Epitaph = Epitaphs.For(record);
        return record;
    }
}

/// <summary>A line for the headstone, chosen by how it ended (the same line every time for the same death).</summary>
public static class Epitaphs
{
    private static readonly string[] Town =
    [
        "Died in the town, of all places. The shopkeepers still talk about it.",
        "Never so much as set foot on the stairs.",
        "Killed within sight of the Prancing Pony.",
    ];

    private static readonly string[] Shallow =
    [
        "Went down the stairs with high hopes and a single torch.",
        "The first steps are the hardest. So are the last.",
        "Brave, young, and not quite careful enough.",
        "Here lies one who thought the shallows were safe.",
    ];

    private static readonly string[] Deep =
    [
        "Went down to {feet} ft, and did not come back.",
        "Went deeper than most, and paid the price for it.",
        "The dark below took them at {feet} ft.",
        "They saw things at {feet} ft that no one should.",
        "Far from the sun, and further from home.",
    ];

    private static readonly string[] VeryDeep =
    [
        "Came within sight of the Iron Crown.",
        "Walked where only heroes walk, and fell as heroes fall.",
        "The songs will remember {name}, who went down to {feet} ft.",
    ];

    private static readonly string[] Victors =
    [
        "Cast down Morgoth, and lived to lay down the sword.",
        "The Iron Crown lies broken. Rest well.",
        "They went into the dark and brought back the dawn.",
    ];

    public static string For(FallenRecord r)
    {
        var feet = r.MaxDepth * 50;
        var lines = r.Won ? Victors : r.MaxDepth == 0 ? Town : r.MaxDepth < 15 ? Shallow : r.MaxDepth < 60 ? Deep : VeryDeep;
        // A stable pick: the same death always gets the same line.
        var hash = 17L;
        foreach (var c in r.Name + r.KilledBy + r.Seed) hash = hash * 31 + c;
        var line = lines[(int)(Math.Abs(hash) % lines.Length)];
        return line.Replace("{feet}", feet.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("{name}", r.Name);
    }
}

/// <summary>
/// The graveyard (AVABand's own, <c>graveyard.json</c> beside the scores): every character that died
/// or retired — not just the hundred best, as the high-score table keeps — newest first. Cheats,
/// the tutorial and replays are left out, as from the scores.
/// </summary>
public sealed class Graveyard
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public List<FallenRecord> Fallen { get; set; } = [];

    /// <summary>Whether the deaths from before there was a graveyard have been brought in (once) from the high scores.</summary>
    public bool Backfilled { get; set; }

    /// <summary>Lays a record in the graveyard (in place of the same end brought in from the scores, if it was).</summary>
    public void Add(FallenRecord record)
    {
        Fallen.RemoveAll(f => SameEnd(f, record));
        Fallen.Insert(0, record);
    }

    private static bool SameEnd(FallenRecord a, FallenRecord b) => a.Name == b.Name && a.Seed == b.Seed && a.DateUtc == b.DateUtc;

    /// <summary>The deaths the high-score table remembers, from before there was a graveyard.</summary>
    public void Backfill(IEnumerable<ScoreEntry> entries)
    {
        foreach (var e in entries.Where(e => !e.IsAlive || e.Won))
            if (FallenRecord.From(e) is var record && !Fallen.Any(f => SameEnd(f, record)))
                Fallen.Add(record);
        Fallen = [.. Fallen.OrderByDescending(f => f.DateUtc)];
        Backfilled = true;
    }

    public static Graveyard Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Graveyard>(File.ReadAllText(path), Json) is { } yard) return yard;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged graveyard starts afresh (the high scores bring the rest back).
        }
        return new Graveyard();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }
}
