using System.Globalization;
using Angband.Core.Definitions;
using Angband.Core.Randomness;

namespace Angband.Core.Game;

/// <summary>A weekly challenge's twist: one rule of the game changed for the week.</summary>
public sealed record WeeklyTwist(string Id, string Name, string Description);

/// <summary>
/// The weekly challenge (AVABand's own): the daily dungeon's idea for a whole week — one seed and one
/// character from the ISO week (UTC), the same for everyone — with a twist on the rules that changes
/// each week. Try as often as you like until the week is out; every try goes on the weekly board.
/// </summary>
public static class WeeklyChallenge
{
    public static readonly WeeklyTwist Darkness = new("darkness", "Darkness", "No room in the dungeon is lit: bring your own light.");

    public static readonly WeeklyTwist GlassCannon = new("glass", "Glass cannon", "You deal half as much damage again, and take half as much again.");

    public static readonly WeeklyTwist Famine = new("famine", "Famine", "Food goes twice as fast: you get hungry in half the time.");

    public static readonly WeeklyTwist Swarm = new("swarm", "Swarm", "Every dungeon level has half as many monsters again.");

    public static readonly WeeklyTwist Restless = new("restless", "Restless foes", "Every monster is a little faster (+2 speed).");

    public static readonly WeeklyTwist GoldRush = new("gold", "Gold rush", "Gold you find is worth twice as much.");

    public static readonly WeeklyTwist LeanTimes = new("lean", "Lean times",
        "Only the General Store will sell to you: the other shops and the caravan won't. Your Home still keeps your things.");

    /// <summary>Every twist, in the order the weeks take them.</summary>
    public static IReadOnlyList<WeeklyTwist> Twists { get; } = [Darkness, GlassCannon, Famine, Swarm, Restless, GoldRush, LeanTimes];

    /// <summary>A twist by its id (as a character keeps it), or null.</summary>
    public static WeeklyTwist? Twist(string? id) => Twists.FirstOrDefault(t => t.Id == id);

    /// <summary>The week, as the weekly challenge counts weeks: ISO weeks (Monday to Sunday), in UTC, so everyone's changes together.</summary>
    public static string ThisWeek(DateTime? utcNow = null) => Stamp(DateOnly.FromDateTime(utcNow ?? DateTime.UtcNow));

    /// <summary>The ISO week a day falls in: "2026-W44" (the year is the week's, so 2027-01-01 is in "2026-W53").</summary>
    public static string Stamp(DateOnly day)
    {
        var date = day.ToDateTime(TimeOnly.MinValue);
        return $"{ISOWeek.GetYear(date):0000}-W{ISOWeek.GetWeekOfYear(date):00}";
    }

    /// <summary>The Monday a week ("2026-W44") begins, or null if it isn't one.</summary>
    public static DateOnly? MondayOf(string week)
    {
        if (week.Length != 8 || week[4] != '-' || week[5] != 'W'
            || !int.TryParse(week.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(week.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || year < 1 || year > 9998 || number < 1 || number > ISOWeek.GetWeeksInYear(year))
            return null;
        return DateOnly.FromDateTime(ISOWeek.ToDateTime(year, number, DayOfWeek.Monday));
    }

    /// <summary>The week's seed: FNV-1a over "AVABand weekly" and the week (the same on every machine).</summary>
    public static ulong SeedFor(string week)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes("AVABand weekly " + week))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    /// <summary>
    /// The week's twist. The twists take turns, three places on each week (seven twists, so a step of
    /// three visits them all): no twist comes two weeks running, and each comes round every seven weeks.
    /// </summary>
    public static WeeklyTwist TwistFor(string week)
    {
        var monday = MondayOf(week) ?? throw new ArgumentException($"Not an ISO week: {week}", nameof(week));
        var index = monday.DayNumber / 7; // (day 0, 1 January 1, was a Monday)
        return Twists[(int)((index * 3L) % Twists.Count)];
    }

    /// <summary>The week's character: race, class and stats from the week's seed; your name.</summary>
    public static CharacterSpec SpecFor(GameData data, string week, string name)
    {
        var rng = new GameRandom(SeedFor(week) ^ 0x5EED_0F_3EE7UL);
        var race = data.Races[rng.RandInt0(data.Races.Count)];
        var cls = data.Classes[rng.RandInt0(data.Classes.Count)];
        var stats = Birth.RollStats(rng);
        return new CharacterSpec(string.IsNullOrWhiteSpace(name) ? "Adventurer" : name, race.Id, cls.Id, stats, StatMethod.Roll);
    }

    /// <summary>Makes a new game the week's: its week and twist, kept with the character (and so in its replay).</summary>
    public static void Prepare(GameSession game, string week)
    {
        game.Player.WeeklyWeek = week;
        game.Player.WeeklyTwist = TwistFor(week).Id;
    }
}
