using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Monsters;
using Angband.Core.Records;
using static Angband.Core.Records.MonsterRecall;

namespace Angband.Tests;

/// <summary>The danger reading: the worst you know a monster can do in a turn, after your resistances, against your life.</summary>
public class DangerReadingTests
{
    private static readonly MonsterRaceDef Dragon = TestData.Game.Monster("ancient_white_dragon")!; // 1500 hp: a 500 frost breath
    private static readonly MonsterRaceDef Soldier = TestData.Game.Monster("soldier")!;           // 1d7, 1d7

    private static RecallViewer You(int hp, int max, int coldResist = 0) => new(id => id == "cold" ? coldResist : 0, 0, hp, max);

    [Fact]
    public void Nothing_known_is_nothing_judged()
    {
        Assert.Equal(DangerLevel.Unknown, Danger(TestData.Game, Dragon, new RaceLore(), You(300, 300)).Level);
        Assert.Contains("You don't yet know enough", RecallMarkup.Strip(DangerMarked(TestData.Game, Dragon, new RaceLore(), You(300, 300))));
        // A breath seen, but its strength unknown until one has died: still nothing to go on.
        Assert.Equal(DangerLevel.Unknown, Danger(TestData.Game, Dragon, new RaceLore { SpellsSeen = { "BR_COLD" } }, You(300, 300)).Level);
    }

    [Fact]
    public void A_known_breath_could_kill_and_a_resistance_cuts_it()
    {
        var lore = new RaceLore { SpellsSeen = { "BR_COLD" }, TotalKills = 1 };
        var (level, damage, what) = Danger(TestData.Game, Dragon, lore, You(300, 300));
        Assert.Equal((DangerLevel.Deadly, 500), (level, damage));
        Assert.Contains("breath", what);
        var text = RecallMarkup.Strip(DangerMarked(TestData.Game, Dragon, lore, You(300, 300)));
        Assert.StartsWith("Danger: Its ", text);
        Assert.Contains("could kill you outright: up to 500, and you have 300 hit points at most.", text);
        Assert.EndsWith("(You may not know all it can do.)", text); // its other spells and blows aren't known

        (level, damage, _) = Danger(TestData.Game, Dragon, lore, You(300, 300, coldResist: 1));
        Assert.Equal((DangerLevel.Serious, 166), (level, damage));
        Assert.Equal(0, Danger(TestData.Game, Dragon, lore, You(300, 300, coldResist: 3)).Damage); // immune
    }

    [Fact]
    public void Known_blows_count_as_a_round_and_hurt_matters()
    {
        var lore = new RaceLore { BlowsSeen = [BlowsForDamage, BlowsForDamage] };
        var (level, damage, what) = Danger(TestData.Game, Soldier, lore, You(300, 300));
        Assert.Equal((DangerLevel.Slight, 14, "blows"), (level, damage, what));
        Assert.DoesNotContain("You may not know", DangerMarked(TestData.Game, Soldier, lore, You(300, 300)));
        Assert.Equal(DangerLevel.Threat, Danger(TestData.Game, Soldier, lore, You(60, 60)).Level);
        Assert.Equal(DangerLevel.Deadly, Danger(TestData.Game, Soldier, lore, You(10, 300)).Level); // badly hurt: deadly now
        Assert.Contains("could kill you as you are now", RecallMarkup.Strip(DangerMarked(TestData.Game, Soldier, lore, You(10, 300))));
    }

    [Fact]
    public void The_recall_and_the_look_line_say_it()
    {
        var game = GameSession.NewGame(TestData.Game, 5, "warrior");
        var lore = game.Lore.For(Dragon.Id);
        lore.SpellsSeen.Add("BR_COLD");
        lore.TotalKills = 1;
        Assert.Contains("Danger: ", game.Recall(Dragon));
        var at = game.Level.AllLocs().First(l => game.Level.IsEmptyFloor(l) && l.DistanceTo(game.Player.Position) > 3);
        var dragon = new MonsterSpawner(game.Data).Place(game.Level, game.Rng, Dragon, at);
        Assert.Contains("could kill you", game.LookDescription(dragon));

        var soldier = new MonsterSpawner(game.Data).Place(game.Level, game.Rng, Soldier,
            game.Level.AllLocs().First(l => game.Level.IsEmptyFloor(l) && l != at && l.DistanceTo(game.Player.Position) > 3));
        Assert.DoesNotContain("could kill you", game.LookDescription(soldier)); // nothing known of it
        Assert.DoesNotContain("dangerous", game.LookDescription(soldier));
    }
}
