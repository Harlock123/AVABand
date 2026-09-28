using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>
/// The egos brought into line with Angband 4.2.5's ego_item.txt, and what came with them: egos
/// for particular kinds (the lanterns'), random extra powers, minimum values, flags taken away,
/// extra might, and the Morgul blades' aggravation and experience drain.
/// </summary>
public class EgoData425Tests
{
    private static EgoItemDef Ego(string id) => TestData.Game.Egos.Single(e => e.Id == id);

    private static Item Make(GameSession game, string kind, string ego, int level = 30)
    {
        var item = game.Objects.Create(kind, 1);
        ObjectFactory.ApplyEgo(game.Rng, item, Ego(ego), level);
        return item;
    }

    [Fact]
    public void Random_values_read_dice_as_Angband_writes_them()
    {
        Assert.Equal((0, 3, 2), (RandomValue.Parse("2d3").Base, RandomValue.Parse("2d3").Dice, RandomValue.Parse("2d3").DiceCount));
        Assert.Equal((5, 5, 1, 10), (RandomValue.Parse("5+d5M10").Base, RandomValue.Parse("5+d5M10").Dice,
            RandomValue.Parse("5+d5M10").DiceCount, RandomValue.Parse("5+d5M10").Bonus));
        var rng = new GameRandom(1);
        var rolls = Enumerable.Range(0, 500).Select(_ => RandomValue.Parse("2d3").Roll(rng, 10)).ToList();
        Assert.Equal(2, rolls.Min());
        Assert.Equal(6, rolls.Max());
    }

    [Fact]
    public void Lanterns_have_egos_of_their_own()
    {
        var game = Arena.Create(3);
        var lantern = game.Data.Object("lantern")!;
        var torch = game.Data.Object("wooden_torch")!;
        Assert.True(Ego("everburning").Fits(lantern));
        Assert.False(Ego("everburning").Fits(torch));
        Assert.True(Ego("of_brightness").Fits(torch));

        var everburning = Make(game, "lantern", "everburning");
        Assert.False(everburning.UsesFuel);
        Assert.DoesNotContain("REFUELABLE", everburning.Flags);
        var bright = Make(game, "wooden_torch", "of_brightness");
        Assert.Equal(game.Data.Object("wooden_torch")!.Modifiers.GetValueOrDefault("light") + 1, bright.Modifier("light"));
    }

    [Fact]
    public void An_everburning_lantern_never_burns_down_and_cannot_be_refilled()
    {
        var game = Arena.Create(4);
        var lantern = game.Player.Inventory.Add(Make(game, "lantern", "everburning"))!;
        game.Execute(new WieldCommand(lantern));
        var fuel = lantern.Fuel;
        for (var i = 0; i < 20; i++) game.Execute(new HoldCommand());
        Assert.Equal(fuel, lantern.Fuel);
        Assert.True(game.Player.LightRadius > 0);
    }

    [Fact]
    public void Some_egos_add_a_random_power()
    {
        var game = Arena.Create(5);
        var high = new[] { "pois", "light", "dark", "sound", "shards", "nexus", "nether", "chaos", "disen" };
        for (var i = 0; i < 10; i++)
        {
            var armour = Make(game, "soft_leather_armour", "of_elvenkind_soft_armour");
            Assert.Contains(armour.Resists, high.Contains);
        }
        var sustained = Make(game, "dagger", "holy_avenger");
        Assert.Contains(sustained.Resists, r => r.StartsWith("sust_", StringComparison.Ordinal));
    }

    [Fact]
    public void Minimums_hold_after_the_magic()
    {
        var game = Arena.Create(6);
        var shadows = Make(game, "lantern", "of_shadows");
        Assert.Equal(-4, Ego("of_shadows").Minimums["light"]);
        Assert.True(shadows.Modifier("light") >= -4);
        shadows.Modifiers["stealth"] = -3;
        ObjectFactory.ApplyEgoMinimums(shadows);
        Assert.Equal(0, shadows.Modifier("stealth"));
    }

    [Fact]
    public void Extra_might_adds_to_the_launchers_multiplier()
    {
        var game = Arena.Create(7);
        var bow = Make(game, "long_bow", "of_extra_might");
        Assert.Equal(game.Data.Object("long_bow")!.Multiplier + 1, bow.Multiplier);
        Assert.Contains($"(x{bow.Multiplier})", ItemNaming.Describe(bow, game.Knowledge, withArticle: false, full: true));
    }

    [Fact]
    public void A_Morgul_blade_aggravates_and_drains_experience()
    {
        var game = Arena.Create(8);
        var blade = game.Player.Inventory.Add(Make(game, "dagger", "morgul"))!;
        game.Execute(new WieldCommand(blade));
        Assert.True(game.Player.HasGearFlag(ItemFlags.Aggravate));
        Assert.True(game.Player.HasGearFlag(ItemFlags.DrainExp));

        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(4, 0), awake: false);
        orc.Sleep = 1000;
        game.RunMonsterTurn(orc);
        Assert.Equal(0, orc.Sleep);

        TestGames.ClearMonsters(game);
        game.Player.Experience = game.Player.MaxExperience = 5000;
        for (var i = 0; i < 300; i++) game.Execute(new HoldCommand());
        Assert.True(game.Player.Experience < 5000, $"{game.Player.Experience}");
        Assert.Equal(5000, game.Player.MaxExperience);
    }

    [Fact]
    public void Split_egos_keep_their_own_values()
    {
        // 4.2.5 has Slay Animal twice: on weapons (100 in 1..30) and on ammunition (80 in 1..40).
        var weapons = Ego("slay_animal");
        var ammo = Ego("slay_animal_shot");
        Assert.Equal((100, 30), (weapons.Commonness, weapons.MaxDepth));
        Assert.Equal((80, 40), (ammo.Commonness, ammo.MaxDepth));
        Assert.Contains("arrow", ammo.Bases);
        Assert.DoesNotContain("arrow", weapons.Bases);
    }
}
