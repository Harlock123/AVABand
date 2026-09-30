using System.Text.Json;

namespace Angband.Core.Records;

/// <summary>One try at a day's daily dungeon: how far it got, and its replay.</summary>
public sealed class DailyEntry
{
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

    /// <summary>A line to share with friends: "AVABand daily 2026-09-30 (try 2): Dain the Dwarf Priest reached 1450 ft…".</summary>
    public string ShareLine() =>
        $"AVABand daily {Day}{(Attempt > 1 ? $" (try {Attempt})" : "")}: {Name} the {Race} {Class} reached {MaxDepth * 50} ft "
        + $"(level {Level}), {Points.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} points — {Fate}.";

    /// <summary>A name for the try's replay file, to send: "AVABand-daily-2026-09-30-try2.avareplay".</summary>
    public string ReplayFileName(string extension) => $"AVABand-daily-{Day}-try{Attempt}{extension}";
}

/// <summary>
/// The daily dungeon's own table (AVABand's own, <c>daily.json</c> beside the scores): every try at
/// every day, newest day first and, within a day, the deepest first. Cheats aren't kept.
/// </summary>
public sealed class DailyBoard
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public List<DailyEntry> Entries { get; set; } = [];

    /// <summary>Adds a try at a day (numbering it: the first try, the second...).</summary>
    public DailyEntry Add(DailyEntry entry)
    {
        entry.Attempt = Entries.Count(e => e.Day == entry.Day) + 1;
        Entries.Add(entry);
        return entry;
    }

    /// <summary>A day's tries, best first: deepest, then most points.</summary>
    public IReadOnlyList<DailyEntry> For(string day) =>
        [.. Entries.Where(e => e.Day == day).OrderByDescending(e => e.MaxDepth).ThenByDescending(e => e.Points)];

    /// <summary>Every try, the newest day first and the best first within it.</summary>
    public IReadOnlyList<DailyEntry> Ordered() =>
        [.. Entries.OrderByDescending(e => e.Day, StringComparer.Ordinal).ThenByDescending(e => e.MaxDepth).ThenByDescending(e => e.Points)];

    public static DailyBoard Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<DailyBoard>(File.ReadAllText(path), Json) is { } board) return board;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged table starts afresh.
        }
        return new DailyBoard();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }
}
