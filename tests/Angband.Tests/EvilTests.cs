using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>Angband 4.2's EVIL player flag (necromancers): nether is resisted, holy orbs hurt more.</summary>
public class EvilTests
{
    private static GameSession Hero(string cls)
    {
        var game = GameSession.NewGame(TestData.Game, 1, cls);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.RecalculateBonuses();
        return game;
    }

    [Fact]
    public void Necromancers_AreEvil_AndResistNether()
    {
        var necro = Hero("necromancer");
        Assert.True(necro.ClassHas("EVIL"));
        Assert.Equal(1, necro.Player.Resists.GetValueOrDefault("nether"));
        Assert.Equal(-1, necro.Player.Resists.GetValueOrDefault("holy_orb"));
        Assert.Equal(0, Hero("mage").Player.Resists.GetValueOrDefault("nether"));
    }

    [Fact]
    public void Nether_HurtsANecromancerLess_AndLeavesTheirExperience()
    {
        var necro = Hero("necromancer");
        necro.GainExperience(50_000);
        var exp = necro.Player.Experience;
        var hp = necro.Player.Hp;
        necro.ElementalHit("nether", 300, "a nether bolt");
        Assert.True(hp - necro.Player.Hp < 300);
        Assert.Equal(exp, necro.Player.Experience);
    }

    [Fact]
    public void AHolyOrb_HurtsTheEvil_AThirdMore()
    {
        var necro = Hero("necromancer");
        var hp = necro.Player.Hp;
        necro.ElementalHit("holy_orb", 30, "a holy orb");
        Assert.Equal(40, hp - necro.Player.Hp);

        var mage = Hero("mage");
        hp = mage.Player.Hp;
        mage.ElementalHit("holy_orb", 30, "a holy orb");
        Assert.Equal(30, hp - mage.Player.Hp);
    }
}
