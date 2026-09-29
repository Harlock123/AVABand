using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>Angband 4.2.5's terrain: lava burns whoever stands in it, and monsters keep out of it.</summary>
public class TerrainData425Tests
{
    [Fact]
    public void Standing_in_lava_burns()
    {
        var game = Arena.Create(3);
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 5000;
        var lava = game.Player.Position + new Loc(1, 0);
        game.Level[lava].Feature = game.Data.Terrain.Ids.Lava;
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.Execute(new WalkCommand(Direction.East));
        game.Execute(new HoldCommand());
        Assert.Contains("The lava burns you!", said);
        Assert.InRange(5000 - game.Player.Hp, 101, 400);
    }

    [Fact]
    public void Monsters_that_fear_fire_keep_out_of_lava()
    {
        var game = Arena.Create(4,
            "#########",
            "#,,,,,,,#",
            "#,@,~,,,#",
            "#,,,,,,,#",
            "#########");
        var lava = game.Player.Position + new Loc(2, 0);
        game.Level[lava].Feature = game.Data.Terrain.Ids.Lava;
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(3, 0));
        for (var i = 0; i < 30; i++)
        {
            game.RunMonsterTurn(orc);
            Assert.NotEqual(lava, orc.Position);
        }
    }

    [Fact]
    public void A_monster_left_in_lava_burns_and_may_disintegrate()
    {
        var game = Arena.Create(5);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(3, 0));
        game.Level[orc.Position].Feature = game.Data.Terrain.Ids.Lava;
        orc.Held = 10_000;
        orc.Hp = orc.MaxHp = 150;
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.UpdateView();
        for (var i = 0; i < 5 && orc.IsActive; i++) game.Execute(new HoldCommand());
        Assert.False(orc.IsActive);
        Assert.Contains(said, m => m.EndsWith("disintegrates!", StringComparison.Ordinal));
    }

    [Fact]
    public void A_step_into_lava_that_would_hurt_badly_asks_first()
    {
        var game = Arena.Create(6);
        TestGames.ClearMonsters(game);
        var lava = game.Player.Position + new Loc(1, 0);
        game.Level[lava].Feature = game.Data.Terrain.Ids.Lava;
        game.Player.Hp = game.Player.MaxHp = 300; // 150 is more than a third
        Assert.Equal("The lava will scald you!  Really step in?", game.DangerousStepWarning(new WalkCommand(Direction.East)));
        game.Player.IntrinsicResists["fire"] = 3;
        game.RecalculateBonuses();
        Assert.Null(game.DangerousStepWarning(new WalkCommand(Direction.East))); // immune: no need
    }

    [Fact]
    public void Running_stops_short_of_lava_and_travel_goes_round_it()
    {
        var game = Arena.Create(7,
            "###########",
            "#,,,,,,,,,#",
            "#,@,,,,,,,#",
            "#,,,,,,,,,#",
            "###########");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 5000;
        var lava = game.Player.Position + new Loc(3, 0);
        game.Level[lava].Feature = game.Data.Terrain.Ids.Lava;
        game.UpdateView();
        game.Execute(new RunCommand(Direction.East));
        Assert.Equal(lava + new Loc(-1, 0), game.Player.Position);
        Assert.Equal(5000, game.Player.Hp);

        var path = game.FindPath(game.Player.Position, lava + new Loc(2, 0));
        Assert.NotNull(path);
        Assert.DoesNotContain(lava, path!);
    }
}
