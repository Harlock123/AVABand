using Angband.Core.Randomness;

namespace Angband.Core.Game;

/// <summary>
/// A curse a level may lie under (AVABand's own): its name, the danger it brings (<paramref name="Risk"/>) and
/// what it gives for it (<paramref name="Reward"/>), each said plainly, so that staying is a choice made knowing both.
/// </summary>
public sealed record LevelCurse(string Id, string Name, string Risk, string Reward);

/// <summary>
/// AVABand's cursed levels (the birth option "Cursed levels"): now and then a new level from 250 ft down lies
/// under a curse — about one in ten, never two in a row, never a quest's level. You arrive on a staircase and
/// are told its terms; stay and take them, or take the stairs and leave it. Its harder monsters and richer
/// treasure are made with the level (GameSession.CursedLevels).
/// </summary>
public static class LevelCurses
{
    /// <summary>One new level in this many is cursed (where one may be).</summary>
    public const int Chance = 10;

    /// <summary>The shallowest a cursed level comes: 250 ft.</summary>
    public const int MinDepth = 5;

    /// <summary>Blood moon: what it adds to every monster's speed.</summary>
    public const int BloodMoonSpeed = 5;

    /// <summary>Blood moon: how many levels deeper the things found on it are made.</summary>
    public const int BloodMoonDepth = 5;

    /// <summary>The feat "Cursed and Kept": turns spent on cursed levels.</summary>
    public const int KeptTurns = 1000;

    /// <summary>The feat "Defied the Curse": how deep (500 ft) and how many slain on it.</summary>
    public const int DefiedDepth = 10, DefiedKills = 30;

    public static readonly LevelCurse Teeming = new("teeming", "Teeming",
        "Half as many monsters again.", "Half as many objects again, every one of them good.");

    public static readonly LevelCurse RestlessDead = new("restless_dead", "Restless dead",
        "Bands of the undead walk here, stronger than the depth.", "Every undead slain here is worth twice the experience.");

    public static readonly LevelCurse BloodMoon = new("blood_moon", "Blood moon",
        $"Every monster here is faster (+{BloodMoonSpeed} speed).", $"Everything found here is made as if {BloodMoonDepth * 50} ft deeper.");

    public static readonly LevelCurse Darkness = new("darkness", "Darkness",
        "Every room is dark, and your light reaches one square less.", "Gold here is worth twice as much, and something excellent lies hidden on the level.");

    public static IReadOnlyList<LevelCurse> All { get; } = [Teeming, RestlessDead, BloodMoon, Darkness];

    public static LevelCurse? Find(string? id) => id is null ? null : All.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// The curse a new level made from this seed lies under, if it may have one: dice of their own, from the
    /// level's seed, so the game's own dice roll exactly as they did whether a level is cursed or not.
    /// </summary>
    public static LevelCurse? Roll(ulong levelSeed)
    {
        var dice = new GameRandom(GameRandom.DeriveSeed(levelSeed, 0xC0125EDL));
        return dice.OneIn(Chance) ? dice.Pick(All) : null;
    }

    /// <summary>The dice a cursed level's monsters and treasure are made with (from its seed: the game's own are untouched).</summary>
    public static GameRandom Dice(ulong levelSeed) => new(GameRandom.DeriveSeed(levelSeed, 0xC0125EDL, 1));
}
