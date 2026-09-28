using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;

namespace Angband.Tests;

/// <summary>Angband 4.2's bodyguards (friends lines with the bodyguard role; mon-move.c get_move_bodyguard).</summary>
public class BodyguardTests
{
    private static GameSession Game(int depth = 1)
    {
        var game = Arena.Create(8,
            "#########################",
            "#,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,@,,,,,,,,,,,,,,,,,,,,,#",
            "#########################");
        if (depth > 1)
        {
            var level = TestLevels.FromAscii(depth, out var eye, [.. Enumerable.Range(0, game.Level.Height).Select(y =>
                new string([.. Enumerable.Range(0, game.Level.Width).Select(x => game.Level.FeatureAt(new Loc(x, y)).Has(Angband.Core.Definitions.TerrainFlags.Passable) ? ',' : '#')]))]);
            game.UseLevel(level, game.Player.Position);
        }
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        return game;
    }

    [Fact]
    public void TheData_MarksTenEscortsAsBodyguards()
    {
        var bolg = TestData.Game.Monster("bolg_son_of_azog")!;
        Assert.Contains(bolg.Friends, f => f.Race == "uruk" && f.IsBodyguard);
        Assert.Equal(10, TestData.Game.Monsters.Sum(m => m.Friends.Count(f => f.IsBodyguard)));
    }

    [Fact]
    public void Bolg_ComesWithUruksGuardingHim()
    {
        var game = Game(depth: 20); // uruks (level 16) come with Bolg only at about their own depth
        var spawner = new MonsterSpawner(game.Data);
        var placed = spawner.PlaceWithFriends(game.Level, game.Rng, game.Data.Monster("bolg_son_of_azog")!, new Loc(12, 5),
            new HashSet<string>());
        var bolg = placed[0];
        var guards = placed.Where(m => m.Race.Id == "uruk").ToList();
        Assert.NotEmpty(guards);
        Assert.All(guards, g => Assert.Equal(bolg.Id, g.BodyguardOf));
        Assert.All(placed.Where(m => m.Race.Id != "uruk"), m => Assert.Null(m.BodyguardOf)); // the others are just his band
    }

    [Fact]
    public void ABodyguard_NeverLosesHeart_WhileItsLeaderLives()
    {
        var game = Game();
        game.Player.Level = 50; // an uruk (level 16) would normally keep well away from a level 50 hero
        var bolg = Arena.AddMonster(game, "bolg_son_of_azog", new Loc(12, 5));
        var uruk = Arena.AddMonster(game, "uruk", new Loc(14, 5));
        Assert.Equal(game.FleeRange, game.CombatRange(uruk).Min);

        uruk.BodyguardOf = bolg.Id;
        Assert.Equal(1, game.CombatRange(uruk).Min);

        // With its leader dead it is an ordinary uruk again.
        game.Level.Monsters.Remove(bolg);
        Assert.Equal(game.FleeRange, game.CombatRange(uruk).Min);
        Assert.Null(uruk.BodyguardOf);
    }

    [Fact]
    public void ABodyguard_StaysByItsLeader_RatherThanChargingThePlayer()
    {
        var game = Game();
        var bolg = Arena.AddMonster(game, "bolg_son_of_azog", new Loc(20, 2), awake: false);
        bolg.Held = 1000; // Bolg stays put
        var uruk = Arena.AddMonster(game, "uruk", new Loc(14, 6));
        uruk.BodyguardOf = bolg.Id;
        game.UpdateView();
        var before = uruk.Position.DistanceTo(bolg.Position);
        for (var i = 0; i < 4; i++) game.RunMonsterTurn(uruk);
        Assert.True(uruk.Position.DistanceTo(bolg.Position) < before, $"{uruk.Position} vs {bolg.Position}");

        // An unbound uruk in the same spot heads for the player instead.
        var game2 = Game();
        var loose = Arena.AddMonster(game2, "uruk", new Loc(14, 6));
        game2.UpdateView();
        var toPlayer = loose.Position.DistanceTo(game2.Player.Position);
        for (var i = 0; i < 4; i++) game2.RunMonsterTurn(loose);
        Assert.True(loose.Position.DistanceTo(game2.Player.Position) < toPlayer);
    }

    [Fact]
    public void TheBond_IsKeptInTheSave()
    {
        var game = Game();
        var bolg = Arena.AddMonster(game, "bolg_son_of_azog", new Loc(12, 5));
        var uruk = Arena.AddMonster(game, "uruk", new Loc(14, 5));
        uruk.BodyguardOf = bolg.Id;
        using var stream = new MemoryStream();
        Angband.Core.Persistence.SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = Angband.Core.Persistence.SaveGame.Load(TestData.Game, stream);
        Assert.Equal(bolg.Id, loaded.Level.Monsters.All.Single(m => m.Race.Id == "uruk").BodyguardOf);
    }
}
