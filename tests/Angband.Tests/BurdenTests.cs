using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>Encumbrance: Angband's rule, and AVABand's (birth_ava_burden) — Strength further, Constitution, worn gear lighter, the burden named.</summary>
public class BurdenTests
{
    private static GameSession Make(bool ava = true, string race = "human", bool races = true) =>
        GameSession.NewGame(TestData.Game, 4, CharacterSpec.Default(race, "warrior")
            with { Options = new Dictionary<string, bool> { [OptionIds.AvaBurden] = ava, [OptionIds.AvaRaces] = races } });

    private static void Stats(GameSession game, int str, int con = 15)
    {
        game.Player.NaturalStats["str"] = str;
        game.Player.NaturalStats["con"] = con;
        game.Player.StatDrain.Clear();
        game.RecalculateBonuses();
    }

    /// <summary>Unhindered carrying, in pounds, at a natural Strength (18/xx as 18 + xx/10).</summary>
    private static double Unhindered(GameSession game, int str, int con = 15)
    {
        Stats(game, str - (game.Player.Stats["str"] - game.Player.NaturalStats.GetValueOrDefault("str", 15)), con);
        return game.Player.WeightLimit / 20.0;
    }

    [Fact]
    public void Strength_counts_from_14_and_never_stops_counting()
    {
        var ava = Make();
        var angband = Make(ava: false);
        Assert.Equal(80, Unhindered(ava, 14));
        Assert.Equal(75, Unhindered(angband, 14)); // Angband's stalls at 13-14
        Assert.Equal(100, Unhindered(ava, 18));
        Assert.Equal(95, Unhindered(angband, 18));
        Assert.Equal(170, Unhindered(ava, 25));    // 18/70
        Assert.Equal(150, Unhindered(angband, 25));
        Assert.Equal(200, Unhindered(ava, 28));    // 18/100
        Assert.Equal(150, Unhindered(angband, 28)); // Angband's stops at 18/70
        Assert.True(Unhindered(ava, 40) > Unhindered(ava, 30));
    }

    [Fact]
    public void Constitution_adds_a_little()
    {
        var game = Make();
        var at15 = Unhindered(game, 18, con: 15);
        Assert.Equal(at15 + 6, Unhindered(game, 18, con: 18)); // 2 lb a step above 15
        Assert.Equal(at15, Unhindered(game, 18, con: 10));      // never less
        var angband = Make(ava: false);
        Assert.Equal(Unhindered(angband, 18, con: 15), Unhindered(angband, 18, con: 18));
    }

    [Fact]
    public void Worn_gear_weighs_three_quarters()
    {
        var ava = Make();
        var worn = ava.Player.Inventory.Equipped.Sum(i => i.TotalWeight);
        Assert.True(worn > 0);
        Assert.Equal(ava.Player.Inventory.TotalWeight - worn + worn * 3 / 4, ava.BurdenWeight);
        var angband = Make(ava: false);
        Assert.Equal(angband.Player.Inventory.TotalWeight, angband.BurdenWeight);
    }

    [Fact]
    public void The_burden_has_a_name_and_a_cost()
    {
        var game = Make();
        Stats(game, 10);
        Assert.Equal("Unhindered", game.BurdenName);
        var speed = game.Player.Speed;
        var tenth = game.Player.WeightLimit / 10;
        while (game.BurdenWeight <= game.Player.WeightLimit / 2 + tenth / 2) game.Player.Inventory.Add(game.Objects.Create("flask_of_oil", 1));
        game.RecalculateBonuses();
        Assert.Equal("Unhindered", game.BurdenName); // over half, but not yet a tenth over: no cost, no name
        while (game.BurdenPenalty < 1) { game.Player.Inventory.Add(game.Objects.Create("flask_of_oil", 1)); game.RecalculateBonuses(); }
        Assert.Equal("Burdened", game.BurdenName);
        Assert.Equal(speed - 1, game.Player.Speed);
        while (game.BurdenPenalty < 2) { game.Player.Inventory.Add(game.Objects.Create("flask_of_oil", 5)); game.RecalculateBonuses(); }
        Assert.Equal("Strained", game.BurdenName);
        while (game.BurdenPenalty < 4) { game.Player.Inventory.Add(game.Objects.Create("flask_of_oil", 5)); game.RecalculateBonuses(); }
        Assert.Equal("Overloaded", game.BurdenName);
        Assert.Equal(speed - game.BurdenPenalty, game.Player.Speed);
    }

    [Fact]
    public void Bags_porters_heroism_and_race_add_up()
    {
        var game = Make();
        Assert.Equal(0, game.CarryPercent);
        game.Player.Inventory.Add(game.Objects.Create("bag_of_holding"));
        var boots = game.Objects.Create("leather_boots");
        boots.Ego = game.Data.Egos.Single(e => e.Id == "porter");
        game.Player.Inventory.Add(boots);
        game.Execute(new WieldCommand(boots));
        Assert.Equal(50 + 20, game.CarryPercent);
        game.IncreaseTimed("hero", 20, check: false);
        Assert.Equal(80, game.CarryPercent);
        var plain = Make(ava: false);
        plain.IncreaseTimed("hero", 20, check: false);
        Assert.Equal(0, plain.CarryPercent); // (Angband's rules: no heroism bonus)

        Assert.Equal(20, Make(race: "half_troll").CarryPercent);
        Assert.Equal(10, Make(race: "dwarf").CarryPercent);
        Assert.Equal(-10, Make(race: "hobbit").CarryPercent);
        Assert.Equal(0, Make(race: "half_troll", races: false).CarryPercent);
    }
}
