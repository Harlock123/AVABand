using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Time;

namespace Angband.Core.Game;

/// <summary>How fed the player is, from worst to best (Angband 4.1's food levels).</summary>
public enum HungerLevel { Starving, Faint, Weak, Hungry, Fed, Full, Gorged }

public static class Hunger
{
    public static HungerLevel LevelOf(int food, GameConstants c) =>
        food < c.FoodStarve ? HungerLevel.Starving
        : food < c.FoodFaint ? HungerLevel.Faint
        : food < c.FoodWeak ? HungerLevel.Weak
        : food < c.FoodHungry ? HungerLevel.Hungry
        : food < c.FoodFull ? HungerLevel.Fed
        : food < c.FoodMax ? HungerLevel.Full
        : HungerLevel.Gorged;

    /// <summary>Angband PY_REGEN_NORMAL/WEAK/FAINT: hit point regeneration by food level.</summary>
    public static int RegenPercent(HungerLevel level, int normal) => level switch
    {
        HungerLevel.Starving => 0,
        HungerLevel.Faint => 33,
        HungerLevel.Weak => 98,
        _ => normal,
    };

    /// <summary>The message on reaching <paramref name="level"/> from <paramref name="from"/>.</summary>
    public static string Message(HungerLevel from, HungerLevel level) => level switch
    {
        HungerLevel.Starving => "You are starving!!",
        HungerLevel.Faint => from < level ? "You are still faint from hunger." : "You are getting faint from hunger!",
        HungerLevel.Weak => from < level ? "You are still weak from hunger." : "You are getting weak from hunger!",
        HungerLevel.Hungry => from < level ? "You are still hungry." : "You are getting hungry.",
        HungerLevel.Fed => from < level ? "You are no longer hungry." : "You are no longer full.",
        HungerLevel.Full => from < level ? "You are full!" : "You are no longer gorged.",
        _ => "You have gorged yourself!",
    };
}

// Hunger: digestion, the food levels and what they do (Angband 4.1 process_world / player_set_food).
public sealed partial class GameSession
{
    public HungerLevel HungerLevel => Hunger.LevelOf(Player.Food, Data.Constants);

    /// <summary>
    /// Sets the food counter, announcing a change of level. Getting hungrier disturbs (stops resting);
    /// being gorged slows the player.
    /// </summary>
    public void SetFood(int value)
    {
        var before = HungerLevel;
        Player.Food = Math.Clamp(value, 0, Data.Constants.FoodUpper);
        var after = HungerLevel;
        if (after == before) return;

        Publish(new MessageEvent(Hunger.Message(before, after)));
        Publish(new HungerChangedEvent(after, Worse: after < before));
        if (after < before && after <= HungerLevel.Hungry) Disturb();
        if (before == HungerLevel.Gorged || after == HungerLevel.Gorged) RecalculateBonuses();
    }

    /// <summary>
    /// Angband 4.1 digestion, every <see cref="GameConstants.DigestInterval"/> game turns: twice the
    /// energy the player gets per game turn, +30 with regeneration, a fifth with slow digestion.
    /// </summary>
    private void Digest()
    {
        var amount = EnergyTable.EnergyPerTurn(Player.Speed) * 2;
        if (Player.Regenerates || Player.HasGearFlag(ItemFlags.Regen)) amount += 30;
        if (Player.HasGearFlag(ItemFlags.SlowDigest)) amount /= 5;
        SetFood(Player.Food - Math.Max(1, amount));
    }

    /// <summary>Hunger upkeep every world tick: digestion, fainting and starvation.</summary>
    private void HungerTick(long gameTurn)
    {
        if (gameTurn % Data.Constants.DigestInterval == 0) Digest();

        var food = Player.Food;
        var c = Data.Constants;
        // Faint: now and then pass out for a few turns.
        if (food < c.FoodFaint && !Player.Timed.Has(TimedIds.Paralyzed) && Rng.OneIn(10)
            && Data.Timed(TimedIds.Paralyzed) is { } paralysis)
        {
            Publish(new MessageEvent("You faint from the lack of food."));
            Disturb();
            Player.Timed.Increase(paralysis, 1 + Rng.RandInt0(5));
            Publish(new StatusChangedEvent(TimedIds.Paralyzed, Player.Timed[TimedIds.Paralyzed]));
        }
        // Starve: damage grows as the counter falls.
        if (food < c.FoodStarve) TakeHit((c.FoodStarve - food) / 10, "starvation", note: false);
    }
}
