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
}
