using Angband.Core.Combat;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>Angband 4.2.5's constants.txt, and what they govern: kits, gold, residents, wanderers, lamps, criticals.</summary>
public class ConstantsData425Tests
{
    [Fact]
    public void The_town_has_four_residents_by_day_and_eight_by_night()
    {
        var day = GameSession.NewGame(TestData.Game, 1, "warrior");
        Assert.True(day.IsDaytime);
        Assert.InRange(day.Level.Monsters.All.Count(), 4, 4 * 25); // four placements (some come with friends)
        Assert.Equal(4, TestData.Game.Constants.TownMonstersDay);
        Assert.Equal(8, TestData.Game.Constants.TownMonstersNight);
    }

    [Fact]
    public void Now_and_then_a_new_monster_turns_up_out_of_sight()
    {
        var game = GameSession.NewGame(TestData.Game, 2, "warrior");
        game.MarkDebugUsed();
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        game.Execute(new DebugJumpCommand(10));
        TestGames.ClearMonsters(game);
        for (var i = 0; i < 3000 && !game.Level.Monsters.All.Any(); i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.Execute(new HoldCommand());
        }
        var newcomer = Assert.Single(game.Level.Monsters.All.Take(1));
        Assert.True(newcomer.Position.DistanceTo(game.Player.Position) > game.Data.Constants.MaxSight + 5 || !newcomer.IsAsleep);
    }

    [Fact]
    public void A_new_lantern_is_half_full()
    {
        var lantern = new ObjectFactory(TestData.Game).Create("lantern");
        Assert.Equal(7500, lantern.Fuel);
        Assert.Equal(15000, lantern.Kind.Fuel);
        Assert.Equal(5000, new ObjectFactory(TestData.Game).Create("wooden_torch").Fuel);
    }

    [Fact]
    public void The_quiver_holds_ten_stacks_and_you_start_with_4_2_5s_gold()
    {
        Assert.Equal(10, TestData.Game.Constants.QuiverSize);
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        var worth = game.Player.Inventory.All.Sum(i => ItemValue.Real(i, game.Data, i.Number));
        Assert.Equal(600 - worth, game.Player.Gold);
        Assert.Contains(game.Player.Inventory.All, i => i.Kind.Id == "potion_of_berserk_strength");
        Assert.Contains(game.Player.Inventory.All, i => i.Kind.Id == "scroll_of_word_of_recall");
    }

    [Fact]
    public void A_debuffed_monster_is_easier_to_crit()
    {
        int Crits(bool debuffed)
        {
            var rng = new GameRandom(5);
            var n = 0;
            for (var i = 0; i < 20000; i++)
            {
                CombatMath.CriticalMelee(rng, 100, 0, 60, 10, out var grade, 20, debuffed);
                if (grade != CriticalGrade.None) n++;
            }
            return n;
        }
        Assert.True(Crits(true) > Crits(false));
    }
}
