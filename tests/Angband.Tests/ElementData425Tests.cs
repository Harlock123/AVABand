using Angband.Core.Combat;
using Angband.Core.Game;
using Angband.Core.Randomness;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>
/// projection.txt's elements as 4.2.5 has them: all 25, resisted by numerator over a denominator
/// rolled once (adjust_dam), ice as cold.
/// </summary>
public class ElementData425Tests
{
    [Fact]
    public void All_25_elements_the_first_13_resistable_by_gear()
    {
        var data = TestData.Game;
        Assert.Equal(25, data.Elements.Count);
        Assert.Equal(13, data.Elements.Count(e => e.Resistable));
        Assert.Equal("cold", data.Element("ice")!.ResistedAs);
        Assert.Equal(200, data.Element("gravity")!.DamageCap);
    }

    [Fact]
    public void The_high_elements_resist_as_six_over_eight_plus_1d4()
    {
        var light = TestData.Game.Element("light")!;
        var rng = new GameRandom(5);
        for (var i = 0; i < 200; i++)
        {
            var d = CombatMath.ResistElement(rng, light, 1200, 1);
            Assert.InRange(d, 1200 * 6 / 12, 1200 * 6 / 9); // 6/(8+1d4)
        }
    }

    [Fact]
    public void Double_resistance_divides_twice_by_one_roll()
    {
        var fire = TestData.Game.Element("fire")!;
        Assert.Equal(900 / 3 / 3, CombatMath.ResistElement(new GameRandom(1), fire, 900, 2));
        var chaos = TestData.Game.Element("chaos")!;
        var rng = new GameRandom(9);
        for (var i = 0; i < 100; i++)
        {
            var d = CombatMath.ResistElement(rng, chaos, 14400, 2);
            // One denominator for both steps: 14400·36/k² for some k in 9..12.
            Assert.Contains(d, new[] { 9, 10, 11, 12 }.Select(k => 14400 * 6 / k * 6 / k));
        }
    }

    [Fact]
    public void Cold_resistance_covers_ice_and_the_sheet_lists_thirteen()
    {
        var game = Arena.Create(4);
        game.Player.Hp = game.Player.MaxHp = 10_000;
        game.Player.IntrinsicResists["cold"] = 1;
        game.RecalculateBonuses();
        var hp = game.Player.Hp;
        game.ElementalHit("ice", 90, "a test");
        Assert.Equal(hp - 30, game.Player.Hp);

        var dump = CharacterDump.Build(game);
        Assert.Contains(" Disenchant", dump);
        Assert.DoesNotContain(" Gravity", dump);
    }
}
