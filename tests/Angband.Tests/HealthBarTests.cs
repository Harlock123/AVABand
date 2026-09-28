using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>Angband's monster health bar (ui-display.c prt_health, monster_health_attr).</summary>
public class HealthBarTests
{
    private static (GameSession Game, Angband.Core.Monsters.Monster Orc) Game()
    {
        var game = Arena.Create(4, "#########", "#,,,@,,,#", "#########");
        TestGames.ClearMonsters(game);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(2, 0));
        orc.Hp = orc.MaxHp = 100;
        orc.Held = 0;
        orc.Sleep = 0;
        game.UpdateView();
        return (game, orc);
    }

    [Fact]
    public void NothingTracked_NoBar_ThenTargetingTracks()
    {
        var (game, orc) = Game();
        Assert.Null(game.HealthBarFor());
        game.SetTarget(orc);
        var bar = game.HealthBarFor()!;
        Assert.Equal(10, bar.Stars);
        Assert.Equal("LightGreen", bar.Color);
        Assert.False(bar.Unknown);
    }

    [Theory]
    [InlineData(100, 10, "LightGreen")]
    [InlineData(95, 10, "Yellow")]
    [InlineData(60, 7, "Yellow")]
    [InlineData(40, 5, "Orange")]
    [InlineData(15, 2, "LightRed")]
    [InlineData(5, 1, "Red")]
    public void TheBar_ShowsTenthsOfHealth_InAngbandsColours(int hp, int stars, string color)
    {
        var (game, orc) = Game();
        game.TrackHealth(orc);
        orc.Hp = hp;
        var bar = game.HealthBarFor()!;
        Assert.Equal(stars, bar.Stars);
        Assert.Equal(color, bar.Color);
    }

    [Fact]
    public void ItsState_ColoursTheBar_AndUnseenItIsUnknown()
    {
        var (game, orc) = Game();
        game.TrackHealth(orc);
        orc.Fear = 5;
        Assert.Equal("Violet", game.HealthBarFor()!.Color);
        orc.Confused = 5;
        Assert.Equal("Umber", game.HealthBarFor()!.Color);
        orc.Stun = 5;
        Assert.Equal("LightBlue", game.HealthBarFor()!.Color);
        orc.Sleep = 5;
        Assert.Equal("Blue", game.HealthBarFor()!.Color);

        orc.IsVisible = false;
        var unseen = game.HealthBarFor()!;
        Assert.True(unseen.Unknown);
        Assert.Equal(0, unseen.Stars);
        Assert.Equal("", unseen.Name); // only the dashes, as in Angband
    }

    [Fact]
    public void StrikingAMonster_TracksIt_AndKillingItEndsTheBar()
    {
        var (game, orc) = Game();
        var near = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(-1, 0));
        near.Hp = near.MaxHp = 1000;
        game.Player.SkillMelee = 1000;
        game.Execute(new WalkCommand(Direction.West));
        Assert.Same(near, game.HealthTracked);
        game.DamageMonster(near, 100_000);
        Assert.Null(game.HealthBarFor());
    }
}
