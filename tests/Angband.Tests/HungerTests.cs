using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Time;

namespace Angband.Tests;

public class HungerTests
{
    private static GameConstants C => TestData.Game.Constants;

    private static GameSession Game()
    {
        var game = Arena.Create(3);
        TestGames.ClearMonsters(game);
        return game;
    }

    [Theory]
    [InlineData(50, HungerLevel.Starving)]
    [InlineData(100, HungerLevel.Faint)]
    [InlineData(700, HungerLevel.Weak)]
    [InlineData(1500, HungerLevel.Hungry)]
    [InlineData(2000, HungerLevel.Fed)]
    [InlineData(9999, HungerLevel.Fed)]
    [InlineData(10000, HungerLevel.Full)]
    [InlineData(15000, HungerLevel.Gorged)]
    public void Levels_follow_angband(int food, HungerLevel expected) => Assert.Equal(expected, Hunger.LevelOf(food, C));

    [Fact]
    public void New_characters_start_just_below_full()
    {
        var game = GameSession.NewGame(TestData.Game, 1);
        Assert.Equal(C.FoodFull - 1, game.Player.Food);
        Assert.Equal(HungerLevel.Fed, game.HungerLevel);
    }

    [Fact]
    public void Normal_speed_digests_20_per_100_game_turns()
    {
        var game = Game();
        game.Player.Food = 5000;
        var start = game.GameTurn;
        TestGames.HoldUntil(game, start + 1000);
        var digestions = (game.GameTurn / C.DigestInterval) - (start / C.DigestInterval);
        Assert.Equal(5000 - 2 * EnergyTable.EnergyPerTurn(0) * digestions, game.Player.Food);
    }

    [Fact]
    public void Slow_digestion_and_regeneration_change_the_rate()
    {
        long Eaten(Action<GameSession> setup)
        {
            var game = Game();
            setup(game);
            game.Player.Food = 9000;
            TestGames.HoldUntil(game, game.GameTurn + 5000);
            return 9000 - game.Player.Food;
        }

        var normal = Eaten(_ => { });
        var slow = Eaten(g => Wear(g, "amulet_of_slow_digestion"));
        var regen = Eaten(g => Wear(g, "ring_of_regeneration"));
        Assert.True(slow * 4 < normal, $"slow {slow} vs normal {normal}");
        Assert.True(regen > normal * 2, $"regen {regen} vs normal {normal}");
    }

    private static void Wear(GameSession game, string kindId)
    {
        var item = game.Objects.Create(game.Data.Object(kindId)!, 1);
        item = game.Player.Inventory.Add(item)!;
        game.Execute(new WieldCommand(item));
        Assert.Contains(item, game.Player.Inventory.Equipped);
    }

    [Fact]
    public void Wearing_slow_digestion_teaches_its_rune()
    {
        var game = Game();
        Wear(game, "amulet_of_slow_digestion");
        Assert.True(game.Knowledge.KnowsRune(RuneIds.Flag(ItemFlags.SlowDigest)));
        Assert.True(game.Player.HasGearFlag(ItemFlags.SlowDigest));
    }

    [Fact]
    public void Getting_hungry_is_announced_and_stops_resting()
    {
        var game = Game();
        var messages = new List<string>();
        var hunger = new List<HungerChangedEvent>();
        using var a = game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        using var b = game.Events.Subscribe<HungerChangedEvent>(hunger.Add);
        game.Player.Food = C.FoodHungry + 5;
        game.Player.Hp = 1;
        game.Player.MaxHp = 1000;

        game.Execute(new RestCommand());
        Assert.Contains("You are getting hungry.", messages);
        Assert.Equal(new HungerChangedEvent(HungerLevel.Hungry, Worse: true), Assert.Single(hunger));
        Assert.True(game.Player.Hp < game.Player.MaxHp); // resting stopped when hunger struck
        Assert.InRange(game.Player.Food, C.FoodHungry - 50, C.FoodHungry - 1);
    }

    [Fact]
    public void Eating_fills_and_gorging_slows()
    {
        var game = Game();
        game.Player.Food = C.FoodHungry - 10;
        var messages = new List<string>();
        using var _ = game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        var speed = game.Player.Speed;

        game.SetFood(game.Player.Food + 6000);
        Assert.Contains("You are no longer hungry.", messages);
        game.SetFood(C.FoodMax + 100);
        Assert.Contains("You have gorged yourself!", messages);
        Assert.Equal(speed - 10, game.Player.Speed);
        game.SetFood(C.FoodMax - 100);
        Assert.Contains("You are no longer gorged.", messages);
        Assert.Equal(speed, game.Player.Speed);
        game.SetFood(99_999);
        Assert.Equal(C.FoodUpper, game.Player.Food);
    }

    [Fact]
    public void Eating_a_ration_uses_the_nourish_effect()
    {
        var game = Game();
        game.Player.Food = 1000;
        var ration = game.Objects.Create(game.Data.Object("ration_of_food")!, 1);
        game.Execute(new UseCommand(game.Player.Inventory.Add(ration)!));
        Assert.InRange(game.Player.Food, 6900, 7000);
    }

    [Fact]
    public void Satisfy_hunger_fills_to_just_below_gorged()
    {
        var game = Game();
        game.Player.Food = 300;
        var scroll = game.Objects.Create(game.Data.Object("satisfy_hunger")!, 1);
        game.Execute(new UseCommand(game.Player.Inventory.Add(scroll)!));
        Assert.InRange(game.Player.Food, C.FoodMax - 30, C.FoodMax - 1);
        Assert.Equal(HungerLevel.Full, game.HungerLevel);
    }

    [Fact]
    public void Weakness_slows_healing_and_starvation_stops_it()
    {
        int Healed(int food)
        {
            var game = Game();
            game.Player.MaxHp = 500;
            game.Player.Hp = 100;
            game.Player.Food = food;
            for (var i = 0; i < 50; i++) game.Execute(new HoldCommand());
            return game.Player.Hp - 100;
        }

        var fed = Healed(5000);
        var weak = Healed(900);
        Assert.True(fed > weak && weak > 0, $"fed {fed}, weak {weak}");
    }

    [Fact]
    public void Starving_hurts_and_can_kill()
    {
        var game = Game();
        game.Player.Food = 0;
        game.Player.Hp = game.Player.MaxHp = 30;
        for (var i = 0; i < 500 && !game.Player.IsDead; i++) game.Execute(new HoldCommand());
        Assert.True(game.Player.IsDead);
        Assert.Equal("starvation", game.Player.KilledBy);
    }

    [Fact]
    public void Faint_characters_pass_out()
    {
        var game = Game();
        game.Player.Food = 200;
        game.Player.Hp = game.Player.MaxHp = 1000;
        var fainted = false;
        using var _ = game.Events.Subscribe<MessageEvent>(m => fainted |= m.Text == "You faint from the lack of food.");
        for (var i = 0; i < 200 && !fainted; i++) game.Execute(new HoldCommand());
        Assert.True(fainted);
    }
}
