using Angband.Core.Definitions;
using Angband.Core.Monsters;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

// The weekly challenge's twists in play (WeeklyChallenge, AVABand's own). Each is on only for a
// character playing the weekly challenge (Player.WeeklyTwist, kept in the save and so in the replay):
// an ordinary game, the daily dungeon and the tutorial play by the usual rules, and roll the dice
// exactly as they always did.
public sealed partial class GameSession
{
    /// <summary>The weekly challenge's twist this character plays under, or null.</summary>
    public WeeklyTwist? Twist => Player.WeeklyTwist is null || IsTutorial ? null : WeeklyChallenge.Twist(Player.WeeklyTwist);

    /// <summary>Whether this twist is on (a quick test, for the busy places).</summary>
    private bool TwistIs(WeeklyTwist twist) => Player.WeeklyTwist is { } id && id == twist.Id && !IsTutorial;

    /// <summary>Glass cannon: the damage you deal a monster, half as much again.</summary>
    private int TwistDamageDealt(int damage) => damage > 0 && TwistIs(WeeklyChallenge.GlassCannon) ? damage + damage / 2 : damage;

    /// <summary>Glass cannon: the damage you take, half as much again (before any damage reduction; the mightiest blows, 9000 and up, kill as they are).</summary>
    private int TwistDamageTaken(int damage) => damage > 0 && damage < 9000 && TwistIs(WeeklyChallenge.GlassCannon) ? damage + damage / 2 : damage;

    /// <summary>Famine: digestion, twice as fast.</summary>
    private int TwistDigestion(int amount) => TwistIs(WeeklyChallenge.Famine) ? amount * 2 : amount;

    /// <summary>Gold rush: gold found, worth twice as much.</summary>
    private int TwistGold(int value) => TwistIs(WeeklyChallenge.GoldRush) ? value * 2 : value;

    /// <summary>Restless foes: what the turn scheduler adds to each monster's speed (+2; nothing for the player).</summary>
    private int TwistSpeedBonus(IActor actor) => Player.WeeklyTwist is not null && actor is Monster && TwistIs(WeeklyChallenge.Restless) ? 2 : 0;

    /// <summary>Lean times: whether a shop (not the General Store, nor your Home) won't sell to you.</summary>
    private bool TwistShopShut(Store store) => !store.IsHome && store.Id != "general" && TwistIs(WeeklyChallenge.LeanTimes);

    /// <summary>Lean times: what a shopkeeper says.</summary>
    internal const string LeanTimesRefusal = "Lean times: only the General Store will sell to you this week.";

    /// <summary>Lean times: whether the caravan won't sell to you.</summary>
    private bool TwistCaravanShut => TwistIs(WeeklyChallenge.LeanTimes);

    /// <summary>
    /// Darkness: a new dungeon level, as generated, has every room's light put out (glowing terrain,
    /// like lava, still glows). Not the town.
    /// </summary>
    private void TwistDarkness()
    {
        if (Player.WeeklyTwist is null || Level.Depth == 0 || !TwistIs(WeeklyChallenge.Darkness)) return;
        PutOutTheLights();
    }

    /// <summary>
    /// Swarm: a new dungeon level, once peopled, gets half as many again of the monsters the level
    /// put about it at random (Angband's pick_and_place_distant_monster ones, not a vault's or a pit's),
    /// each with its friends, asleep and well away from you. Not the town.
    /// </summary>
    private void TwistSwarm(ISet<string> unavailableUniques)
    {
        if (Player.WeeklyTwist is null || Level.Depth == 0 || !TwistIs(WeeklyChallenge.Swarm)) return;
        var placed = Level.SpawnHints.Count(h => h.Kind == SpawnKind.Monster && h.DepthBonus == 0 && h.Tag == "sleep,group");
        for (var i = 0; i < (placed + 1) / 2; i++) _spawner.PlaceDistant(Level, Rng, Player.Position, 10, Level.Depth, unavailableUniques);
    }
}
