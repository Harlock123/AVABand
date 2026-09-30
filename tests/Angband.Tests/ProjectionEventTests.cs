using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>What the game reports for the map to animate: projections, and damage done to monsters.</summary>
public class ProjectionEventTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(4,
            "###############",
            "#,,,,,,,,,,,,,#",
            "#,,,,,,@,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "###############");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        return game;
    }

    [Fact]
    public void AMonstersBreath_IsReportedAsAConeCoveringThePlayer()
    {
        var game = Game();
        var dragon = Arena.AddMonster(game, "baby_red_dragon", game.Player.Position + new Loc(-5, 0));
        dragon.Held = 1000;
        game.UpdateView();
        var shown = new List<ProjectionEvent>();
        game.Events.Subscribe<ProjectionEvent>(shown.Add);
        game.CastSpellForTest(dragon, "BR_FIRE");
        var breath = Assert.Single(shown);
        Assert.Equal(ProjectionKind.Breath, breath.Kind);
        Assert.Equal("fire", breath.Element);
        Assert.Equal(dragon.Position, breath.From);
        Assert.Contains(game.Player.Position, breath.Burst);
    }

    [Fact]
    public void AMonstersBolt_FliesToThePlayer()
    {
        var game = Game();
        var caster = Arena.AddMonster(game, "mage", game.Player.Position + new Loc(4, 0));
        caster.Held = 1000;
        game.UpdateView();
        var shown = new List<ProjectionEvent>();
        game.Events.Subscribe<ProjectionEvent>(shown.Add);
        game.CastSpellForTest(caster, "BO_COLD");
        var bolt = Assert.Single(shown);
        Assert.Equal(ProjectionKind.Bolt, bolt.Kind);
        Assert.Equal(game.Player.Position, bolt.Path[^1]);
        Assert.Empty(bolt.Burst);
    }

    [Fact]
    public void AnArrow_IsShownOnlyAsFarAsItFlew_AndItsDamageIsReported()
    {
        var game = Game();
        var jackal = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(3, 0));
        jackal.Hp = jackal.MaxHp = 10_000;
        jackal.Held = 1000;
        game.Player.SkillThrow = 500;
        game.UpdateView();
        var shown = new List<ProjectionEvent>();
        var hurt = new List<MonsterDamagedEvent>();
        game.Events.Subscribe<ProjectionEvent>(shown.Add);
        game.Events.Subscribe<MonsterDamagedEvent>(hurt.Add);
        // (Even a sure hand misses now and then, as in Angband: a few throws until one lands.)
        for (var i = 0; i < 10 && hurt.Count == 0; i++)
        {
            shown.Clear();
            var dagger = game.Objects.Create("dagger");
            game.Player.Inventory.Add(dagger);
            game.Execute(new ThrowCommand(dagger, jackal.Position));
        }
        var flight = Assert.Single(shown);
        Assert.Equal(ProjectionKind.Missile, flight.Kind);
        Assert.Equal(jackal.Position, flight.Path[^1]); // stopped at the jackal, not the full range
        var damage = Assert.Single(hurt);
        Assert.Equal(jackal.Id, damage.MonsterId);
        Assert.True(damage.Damage > 0);
    }
}
