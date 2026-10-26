namespace Angband.Core.Game;

/// <summary>
/// A challenge run (AVABand's): a harder way to play, made of birth options, with its own feats and a tag
/// on the high scores and the headstone. It's on when every option it names has the value given.
/// </summary>
public sealed record ChallengeDef(string Id, string Name, string Description, IReadOnlyDictionary<string, bool> Options)
{
    public bool IsOn(Func<string, bool> value) => Options.All(o => value(o.Key) == o.Value);
}

/// <summary>A difficulty preset for the creation screen: values for the birth options that make the game easier or harder.</summary>
public sealed record DifficultyPreset(string Id, string Name, string Description, IReadOnlyDictionary<string, bool> Options)
{
    public bool Matches(Func<string, bool> value) => Options.All(o => value(o.Key) == o.Value);
}

/// <summary>AVABand's challenge runs and difficulty presets (Help → Birth options).</summary>
public static class Challenges
{
    /// <summary>A speed run's goal: this deep (1000 ft)...</summary>
    public const int SpeedRunDepth = 20;

    /// <summary>...within this many turns at normal speed.</summary>
    public const long SpeedRunTurns = 15_000;

    public static readonly ChallengeDef Ironman = new("ironman", "Ironman",
        "No way back: every staircase leads down and Word of Recall does nothing. You won't see the town again.",
        new Dictionary<string, bool> { [OptionIds.ForceDescend] = true, [OptionIds.NoRecall] = true });

    public static readonly ChallengeDef ForcedDescent = new("descent", "Forced descent",
        "Every staircase leads down, to one level below your deepest. Word of Recall still takes you to town and back.",
        new Dictionary<string, bool> { [OptionIds.ForceDescend] = true });

    public static readonly ChallengeDef NoShopping = new("noshop", "No shopping",
        "The shops and the caravan won't sell to you, and the strongroom is shut: you keep what you find. Your Home still keeps your things.",
        new Dictionary<string, bool> { [OptionIds.AvaNoShopping] = true });

    public static readonly ChallengeDef SpeedRun = new("speed", "Speed run",
        $"Reach {SpeedRunDepth * 50} ft within {SpeedRunTurns:N0} turns. The status bar counts them down.",
        new Dictionary<string, bool> { [OptionIds.AvaSpeedRun] = true });

    public static IReadOnlyList<ChallengeDef> All { get; } = [Ironman, ForcedDescent, NoShopping, SpeedRun];

    /// <summary>The challenges a set of options makes, by name (an Ironman's forced descent goes without saying).</summary>
    public static IReadOnlyList<string> Names(Func<string, bool> value)
    {
        var ironman = Ironman.IsOn(value);
        return [.. All.Where(c => c.IsOn(value) && !(ironman && c == ForcedDescent)).Select(c => c.Name)];
    }

    /// <summary>A tag for the scores and the graveyard (" [Ironman, Speed run]"), or "" for none.</summary>
    public static string Tag(IReadOnlyCollection<string>? names) => names is { Count: > 0 } ? $" [{string.Join(", ", names)}]" : "";

    /// <summary>The presets, easiest first. Classic is the game's defaults.</summary>
    public static IReadOnlyList<DifficultyPreset> Presets { get; } =
    [
        new("relaxed", "Relaxed", "Everything known from the start, connected stairs, easier casting, hirelings, monsters that don't learn your weak spots, and a second chance if you fall early on.",
            Preset(connect: true, aiLearn: false, know: true, easyCasting: true, hirelings: true, secondChance: true)),
        new("classic", "Classic", "AVABand as it comes: connected stairs, easier casting and hirelings; monsters learn, and you learn runes and flavours as you go.",
            Preset(connect: true, aiLearn: true, know: false, easyCasting: true, hirelings: true, secondChance: false)),
        new("hard", "Hard", "Angband's casting, no connected stairs and no hirelings; monsters learn, and you learn everything the hard way.",
            Preset(connect: false, aiLearn: true, know: false, easyCasting: false, hirelings: false, secondChance: false)),
    ];

    private static Dictionary<string, bool> Preset(bool connect, bool aiLearn, bool know, bool easyCasting, bool hirelings, bool secondChance) => new()
    {
        [OptionIds.ConnectStairs] = connect, [OptionIds.AiLearn] = aiLearn, [OptionIds.KnowRunes] = know, [OptionIds.KnowFlavors] = know,
        [OptionIds.AvaEasyCasting] = easyCasting, [OptionIds.Hirelings] = hirelings, [OptionIds.AvaSecondChance] = secondChance,
    };

    /// <summary>The preset these options match, or null (custom).</summary>
    public static DifficultyPreset? PresetFor(Func<string, bool> value) => Presets.FirstOrDefault(p => p.Matches(value));
}

// The challenges in play (Challenges): no shopping and the speed run's clock. Ironman and forced
// descent are Angband's own birth options (GameSession.Options). None of them in the daily dungeon or
// the tutorial.
public sealed partial class GameSession
{
    private bool ChallengesApply => Player.DailyDate is null && !IsTutorial;

    /// <summary>The challenges this character took on, by name.</summary>
    public IReadOnlyList<string> ChallengeNames => ChallengesApply ? Challenges.Names(id => Options[id]) : [];

    public bool NoShopping => ChallengesApply && Options[OptionIds.AvaNoShopping];

    public bool SpeedRunOn => ChallengesApply && Options[OptionIds.AvaSpeedRun];

    /// <summary>The speed run was made: 1000 ft in time.</summary>
    public bool SpeedRunMade => SpeedRunOn && Player.SpeedRunResult > 0;

    /// <summary>"no shopping": what a shopkeeper says to a character who swore off buying.</summary>
    internal const string NoShoppingRefusal = "You swore to keep only what you find (no shopping).";

    /// <summary>The speed run for the status bar ("speed run: 12,345 left"), or null without one.</summary>
    public string? SpeedRunStatus => !SpeedRunOn ? null
        : Player.SpeedRunResult > 0 ? $"speed run made in {Player.SpeedRunResult:N0}"
        : Player.SpeedRunResult < 0 ? "speed run: out of time"
        : $"speed run: {Math.Max(0, Challenges.SpeedRunTurns - NormalTurns):N0} left";

    /// <summary>Each world tick: the speed run reached its depth in time, or ran out of it.</summary>
    private void SpeedRunUpkeep()
    {
        if (!SpeedRunOn || Player.SpeedRunResult != 0) return;
        var feet = Challenges.SpeedRunDepth * Data.Constants.FeetPerLevel;
        if (Player.MaxDepth >= Challenges.SpeedRunDepth && NormalTurns <= Challenges.SpeedRunTurns)
        {
            Player.SpeedRunResult = Math.Max(1, NormalTurns);
            AddHistory($"Made the speed run: {feet} ft in {NormalTurns:N0} turns");
            Publish(new MessageEvent($"Speed run made: {feet} ft in {NormalTurns:N0} turns!"));
        }
        else if (NormalTurns > Challenges.SpeedRunTurns)
        {
            Player.SpeedRunResult = -1;
            AddHistory($"Ran out of time on the speed run ({Player.MaxDepth * Data.Constants.FeetPerLevel} ft)");
            Publish(new MessageEvent($"Your speed run is over: {Challenges.SpeedRunTurns:N0} turns, and you never reached {feet} ft."));
        }
    }
}
