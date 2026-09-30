using System.Globalization;
using Angband.Core.Definitions;
using Angband.Core.Randomness;

namespace Angband.Core.Game;

/// <summary>
/// The daily dungeon (AVABand's own): one seed a day, the same for everyone, and a starting
/// character drawn from it — race, class and rolled stats — with the default birth options. The
/// game is deterministic, so the same AVABand gives everyone the same dungeon that day: compare how
/// far you got, and swap replays.
/// </summary>
public static class DailyDungeon
{
    /// <summary>The day, as the daily dungeon counts days: UTC, so everyone's changes together.</summary>
    public static DateOnly Today(DateTime? utcNow = null) => DateOnly.FromDateTime(utcNow ?? DateTime.UtcNow);

    public static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The day's seed: FNV-1a over "AVABand daily" and the date (the same on every machine).</summary>
    public static ulong SeedFor(DateOnly day)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes("AVABand daily " + Stamp(day)))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    /// <summary>The day's character: race, class and stats from the day's seed; your name.</summary>
    public static CharacterSpec SpecFor(GameData data, DateOnly day, string name)
    {
        var rng = new GameRandom(SeedFor(day) ^ 0x5EED_0F_DA11UL);
        var race = data.Races[rng.RandInt0(data.Races.Count)];
        var cls = data.Classes[rng.RandInt0(data.Classes.Count)];
        var stats = Birth.RollStats(rng);
        return new CharacterSpec(string.IsNullOrWhiteSpace(name) ? "Adventurer" : name, race.Id, cls.Id, stats, StatMethod.Roll);
    }
}
