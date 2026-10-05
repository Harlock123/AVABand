using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>The statistics page's tallies (AVABand's own).</summary>
public class StatsTests
{
    [Fact]
    public void Fighting_quaffing_and_close_calls_are_counted_and_saved()
    {
        var game = Arena.Create(3,
            "#######",
            "#@,,,,#",
            "#######");
        var jackal = Arena.AddMonster(game, "jackal", new Loc(2, 1));
        jackal.Hp = 1;
        game.Execute(new WalkCommand(Direction.East));                         // attack until it dies
        for (var i = 0; i < 20 && game.Level.Monsters.All.Contains(jackal); i++) game.Execute(new WalkCommand(Direction.East));
        Assert.True(game.Stats.DamageDealt > 0);
        Assert.True(game.Stats.BiggestHit > 0);
        Assert.Equal(1, game.TopKills().Single(k => k.Name == "jackal").Count);

        var potion = game.Player.Inventory.Add(game.Objects.Create("cure_light_wounds"))!;
        game.Execute(new UseCommand(potion));
        Assert.Equal(1, game.Stats.PotionsQuaffed);

        game.Player.Hp = game.Player.MaxHp;
        game.TakeHit(game.Player.MaxHp - 1, "a test");                          // down to 1: a close call, and lived
        Assert.Equal(1, game.Stats.CloseCalls);
        Assert.Equal(game.Player.MaxHp - 1, game.Stats.WorstHitTaken);

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(game.Stats.DamageDealt, loaded.Stats.DamageDealt);
        Assert.Equal(1, loaded.Stats.PotionsQuaffed);
        Assert.Equal(1, loaded.Stats.CloseCalls);
    }

    [Fact]
    public void Stairs_are_counted()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        game.Player.Position = game.Level.FindFeature(Angband.Core.Definitions.TerrainFlags.DownStair).First();
        game.Execute(new TakeStairsCommand(Down: true));
        Assert.Equal(1, game.Stats.StairsTaken);
    }
}
