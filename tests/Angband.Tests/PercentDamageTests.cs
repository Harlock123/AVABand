using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2's birth_percent_damage ("To-damage is a percentage of dice", O-combat,
/// player-attack.c o_melee_damage / o_ranged_damage).
/// </summary>
public class PercentDamageTests
{
    private static GameSession Game(bool on = true)
    {
        var game = Arena.Create(4, "#########", "#,,,@,,,#", "#########");
        TestGames.ClearMonsters(game);
        game.Options[OptionIds.PercentDamage] = on;
        game.Options[OptionIds.AngbandBlows] = false; // (these set the blows by hand: the class's fixed figure)
        game.RecalculateBonuses();
        return game;
    }

    private static double Average(Func<int> roll, int n = 20_000)
    {
        long sum = 0;
        for (var i = 0; i < n; i++) sum += roll();
        return (double)sum / n;
    }

    [Fact]
    public void TheOption_IsABirthOption_OffByDefault()
    {
        var option = OptionCatalog.Find(OptionIds.PercentDamage)!;
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.False(option.Default);
        Assert.Equal("To-damage is a percentage of dice (experimental)", option.Description);
    }

    [Fact]
    public void WithNoBonus_TheDiceAreUnchanged()
    {
        var game = Game();
        // 2d5 at +0: the die average (3) needs 5 sides, exactly.
        for (var i = 0; i < 200; i++) Assert.InRange(game.ODamage(2, 5, 1, 10, 0, 0), 2, 10);
        Assert.InRange(Average(() => game.ODamage(2, 5, 1, 10, 0, 0)), 5.9, 6.1);
    }

    [Fact]
    public void ToDam_MakesTheDiceBigger_ByTheDeadlinessPercentage()
    {
        var game = Game();
        // +10 is +39%: a die averaging 3 averages 4.17, so 2d5 becomes about 2d7.34 (average 8.34),
        // where the flat bonus would have made it 16.
        Assert.InRange(Average(() => game.ODamage(2, 5, 1, 10, 10, 0)), 8.1, 8.6);
        // A negative bonus shrinks them.
        Assert.True(Average(() => game.ODamage(2, 5, 1, 10, -10, 0)) < 5);
    }

    [Fact]
    public void Slays_AndBrands_MultiplyTheDice_AndAddTheirExtra()
    {
        var game = Game();
        // Slay orc (x3 -> O x2.5): each die averages 7.5 (2d14), plus 15.
        Assert.InRange(Average(() => game.ODamage(2, 5, 1, 25, 0, 0)), 29.5, 30.5);
        Assert.Equal(18, game.OSlayMultiplier("EVIL", 2));
        Assert.Equal(20, game.OSlayMultiplier("ANIMAL", 2));
        Assert.Equal(25, game.OSlayMultiplier("ORC", 3));
        Assert.Equal(35, game.OSlayMultiplier("DRAGON", 5));
        Assert.Equal(15, game.OBrandMultiplier("fire", 2, vulnerable: false));
        Assert.Equal(25, game.OBrandMultiplier("fire", 3, vulnerable: false));
        Assert.Equal(40, game.OBrandMultiplier("fire", 3, vulnerable: true)); // twice the extra
    }

    [Fact]
    public void ACritical_AddsWholeDice()
    {
        var game = Game();
        Assert.InRange(Average(() => game.ODamage(2, 5, 1, 10, 0, 2)), 11.8, 12.2); // four dice
    }

    [Fact]
    public void Everyone_HasTwoBlowsAtLeast()
    {
        var off = Game(on: false);
        off.Player.BaseBlows = 110; // a weak character's 1.1 blows
        off.RecalculateBonuses();
        Assert.Equal(110, off.Player.Blows);

        var on = Game();
        on.Player.BaseBlows = 110;
        on.RecalculateBonuses();
        Assert.Equal(200, on.Player.Blows);
    }

    [Fact]
    public void ABlow_TakesToDamAsAPercentage_NotAFlatBonus()
    {
        static double MeanBlow(bool percent)
        {
            var game = Game(percent);
            var dagger = game.Player.Inventory.Weapon!; // 1d4
            dagger.ToDam = 20;
            game.Player.SkillMelee = 1000; // always hits
            game.RecalculateBonuses();
            var damages = new List<int>();
            game.Events.Subscribe<PlayerAttackEvent>(e => { if (e.Hit) damages.Add(e.Damage); });
            var m = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(1, 0));
            m.Held = 1000;
            for (var i = 0; i < 300; i++)
            {
                m.Hp = m.MaxHp = 1_000_000;
                game.Execute(new WalkCommand(Direction.East));
            }
            return damages.Average();
        }

        // Classic: 1d4 + 20 and more, every blow. Percent: +20 is +72%, so the die averages 4.3.
        Assert.True(MeanBlow(percent: false) > 20);
        Assert.InRange(MeanBlow(percent: true), 3, 12);
    }
}
