using System.Text.Json;

namespace Angband.Core.Records;

/// <summary>One try at a day's daily dungeon: how far it got, and its replay.</summary>
public class DailyEntry
{
    /// <summary>The day ("2026-09-30"); for a weekly try, the week ("2026-W44").</summary>
    public string Day { get; set; } = "";
    public int Attempt { get; set; }
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public int Level { get; set; }
    public int MaxDepth { get; set; }
    public long Points { get; set; }
    public long Turns { get; set; }
    public string Fate { get; set; } = "";
    public DateTime DateUtc { get; set; }
    public string? ReplayPath { get; set; }
    /// <summary>The mods the try was played with (AVABand's own): not the same dungeon as everyone else's. Null for none.</summary>
    public List<string>? Mods { get; set; }

    /// <summary>A line to share with friends: "AVABand daily 2026-09-30 (try 2): Dain the Dwarf Priest reached 1450 ft…".</summary>
    public virtual string ShareLine() =>
        $"AVABand daily {Day}{(Attempt > 1 ? $" (try {Attempt})" : "")}{Scoring.ModdedTag(Mods)}: {Name} the {Race} {Class} reached {MaxDepth * 50} ft "
        + $"(level {Level}), {Points.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} points — {Fate}.";

    /// <summary>A name for the try's replay file, to send: "AVABand-daily-2026-09-30-try2.avareplay".</summary>
    public virtual string ReplayFileName(string extension) => $"AVABand-daily-{Day}-try{Attempt}{extension}";
}

/// <summary>One try at a week's weekly challenge (its <see cref="DailyEntry.Day"/> is the week, "2026-W44"), with the week's twist.</summary>
public sealed class WeeklyEntry : DailyEntry
{
    /// <summary>The week's twist, by name ("Darkness").</summary>
    public string Twist { get; set; } = "";

    public override string ShareLine() => ShareLine(Attempt);

    /// <summary>
    /// A line to share: "AVABand weekly 2026-W44 (Darkness): Bob the Human Warrior reached 1250 ft
    /// (level 18), 9,876 points — killed by a cave troll at 1250 ft; 3 tries."
    /// </summary>
    /// <param name="tries">How many tries the week has had.</param>
    public string ShareLine(int tries) =>
        $"AVABand weekly {Day}{(Twist.Length > 0 ? $" ({Twist})" : "")}{Scoring.ModdedTag(Mods)}: {Name} the {Race} {Class} reached {MaxDepth * 50} ft "
        + $"(level {Level}), {Points.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} points — {Fate}; "
        + $"{tries} {(tries == 1 ? "try" : "tries")}.";

    /// <summary>A name for the try's replay file: "AVABand-weekly-2026-W44-try2.avareplay".</summary>
    public override string ReplayFileName(string extension) => $"AVABand-weekly-{Day}-try{Attempt}{extension}";
}

/// <summary>
/// A table of tries at a seeded run (AVABand's own): every try at every day (or week), the newest
/// first and, within a day, the deepest first. Cheats aren't kept.
/// </summary>
public abstract class TryBoard<TEntry> where TEntry : DailyEntry
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public List<TEntry> Entries { get; set; } = [];

    /// <summary>Adds a try at a day (numbering it: the first try, the second...).</summary>
    public TEntry Add(TEntry entry)
    {
        entry.Attempt = Entries.Count(e => e.Day == entry.Day) + 1;
        Entries.Add(entry);
        return entry;
    }

    /// <summary>A day's tries, best first: deepest, then most points.</summary>
    public IReadOnlyList<TEntry> For(string day) =>
        [.. Entries.Where(e => e.Day == day).OrderByDescending(e => e.MaxDepth).ThenByDescending(e => e.Points)];

    /// <summary>Every try, the newest day first and the best first within it.</summary>
    public IReadOnlyList<TEntry> Ordered() =>
        [.. Entries.OrderByDescending(e => e.Day, StringComparer.Ordinal).ThenByDescending(e => e.MaxDepth).ThenByDescending(e => e.Points)];

    protected static TBoard Load<TBoard>(string path) where TBoard : TryBoard<TEntry>, new()
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<TBoard>(File.ReadAllText(path), Json) is { } board) return board;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged table starts afresh.
        }
        return new TBoard();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, GetType(), Json));
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>The daily dungeon's own table (<c>daily.json</c> beside the scores): every try at every day.</summary>
public sealed class DailyBoard : TryBoard<DailyEntry>
{
    public static DailyBoard Load(string path) => Load<DailyBoard>(path);
}

/// <summary>The weekly challenge's own table (<c>weekly.json</c> beside the scores): every try at every week.</summary>
public sealed class WeeklyBoard : TryBoard<WeeklyEntry>
{
    public static WeeklyBoard Load(string path) => Load<WeeklyBoard>(path);
}
