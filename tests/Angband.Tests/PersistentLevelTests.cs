using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Persistence;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>Angband 4.2's birth_levels_persist: levels are kept, and their stairs meet.</summary>
public class PersistentLevelTests
{
    private static GameSession Game(bool persist = true, ulong seed = 11)
    {
        var game = GameSession.NewGame(TestData.Game, seed, "warrior");
        game.Options[OptionIds.LevelsPersist] = persist;
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        TestGames.ClearMonsters(game);
        return game;
    }

    /// <summary>Stands on a staircase of the kind asked for and takes it.</summary>
    private static Loc TakeStairs(GameSession game, bool down, bool clear = true)
    {
        var flag = down ? TerrainFlags.DownStair : TerrainFlags.UpStair;
        var stair = game.Level.FindFeature(flag).First();
        game.Player.Position = stair;
        if (clear) TestGames.ClearMonsters(game);
        Assert.True(game.Execute(new TakeStairsCommand(down)));
        return stair;
    }

    [Fact]
    public void TheOption_IsAngbands_OffByDefault()
    {
        var option = OptionCatalog.Find(OptionIds.LevelsPersist)!;
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.False(option.Default);
        Assert.Equal("Persistent levels (experimental)", option.Description);
    }

    [Fact]
    public void GoingBack_FindsTheSameLevel_WithWhatWasLeftOnIt()
    {
        var game = Game();
        TakeStairs(game, down: true);                     // town -> 1
        var level1 = game.Level;
        var dropped = game.Objects.Create("dagger");
        game.Level.Objects.Add(game.Player.Position + new Loc(0, 0), dropped);
        var leftFrom = TakeStairs(game, down: true);      // 1 -> 2
        Assert.Equal(2, game.Player.Depth);
        Assert.Equal(leftFrom, game.Player.Position);    // the up staircase right under the one taken

        TakeStairs(game, down: false);                    // 2 -> 1
        Assert.Same(level1, game.Level);
        Assert.Contains(game.Level.Objects.All, o => ReferenceEquals(o.Item, dropped));
        Assert.True(game.Level.Has(game.Player.Position, TerrainFlags.DownStair)); // back where the way down is
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    public void EveryWayDown_ComesOutOnTheLevelBelow_EvenWhereItDoesNotFit(int seed)
    {
        // A join in permanent rock (a vault's wall) is moved to the nearest square that fits.
        var game = Game(seed: (ulong)seed);
        TakeStairs(game, down: true);
        var downs = game.Level.FindFeature(TerrainFlags.DownStair).ToList();
        TakeStairs(game, down: true);
        var ups = game.Level.FindFeature(TerrainFlags.UpStair).ToList();
        Assert.Equal(downs.Count, ups.Count);
        Assert.All(downs, d => Assert.Contains(ups, u => u.DistanceTo(d) <= 10));
    }

    [Fact]
    public void ANewLevel_MeetsItsNeighbours_Stairs()
    {
        // (A join in permanent rock is moved to the nearest square that fits — see the test above —
        // so the squares match exactly only where all fit, as they do for this seed.)
        var game = Game(seed: 12);
        TakeStairs(game, down: true);                     // town -> 1
        var downs = game.Level.FindFeature(TerrainFlags.DownStair).ToHashSet();
        TakeStairs(game, down: true);                     // 1 -> 2 (new, built to meet level 1)
        var ups = game.Level.FindFeature(TerrainFlags.UpStair).ToHashSet();
        Assert.Equal(downs, ups);                         // every way down from 1 comes out here, and nothing else goes up
    }

    [Fact]
    public void WithoutTheOption_LevelsAreNotKept()
    {
        var game = Game(persist: false);
        TakeStairs(game, down: true);
        var level1 = game.Level;
        TakeStairs(game, down: true);
        TakeStairs(game, down: false);
        Assert.NotSame(level1, game.Level);
        Assert.Empty(game.StoredLevels);
    }

    [Fact]
    public void MonstersOnAKeptLevel_Recover_WhileYouAreAway()
    {
        var game = Game();
        TakeStairs(game, down: true);
        var jackal = Arena.AddMonster(game, "jackal", game.Level.AllLocs().First(p => game.Level.IsEmptyFloor(p)
            && p.DistanceTo(game.Player.Position) > 5));
        jackal.MaxHp = 100;
        jackal.Hp = 10;
        jackal.Confused = 50;
        TakeStairs(game, down: true, clear: false);
        for (var i = 0; i < 300; i++) game.Execute(new HoldCommand()); // 3000 game turns
        TakeStairs(game, down: false);
        Assert.True(jackal.Hp > 10);
        Assert.Equal(0, jackal.Confused);
    }

    [Fact]
    public void KeptLevels_AreSaved_AndComeBackAfterLoading()
    {
        var game = Game();
        TakeStairs(game, down: true);
        var layout = game.Level.AllLocs().Select(p => game.Level[p].Feature).ToArray();
        TakeStairs(game, down: true);
        Assert.Equal([0, 1], game.StoredLevels.Keys.Order());

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal([0, 1], loaded.StoredLevels.Keys.Order());
        TakeStairs(loaded, down: false);
        Assert.Equal(layout, loaded.Level.AllLocs().Select(p => loaded.Level[p].Feature).ToArray());
    }

    [Fact]
    public void RegeneratingTheLevel_MakesANewOne_EvenWhenLevelsPersist()
    {
        var game = Game();
        TakeStairs(game, down: true);
        var level1 = game.Level;
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(1));
        Assert.NotSame(level1, game.Level);
        Assert.False(game.StoredLevels.ContainsKey(1));
    }

    [Fact]
    public void TheGenerator_PutsJoinsInRock_AndReachesThem_AndSkipsLabyrinths()
    {
        var generator = new DungeonGenerator(TestData.Game);
        var joins = new[] { new StairJoin(new Loc(12, 10), Down: false), new StairJoin(new Loc(150, 50), Down: true) };
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var level = generator.Generate(new LevelRequest(5, seed, ProfileId: "classic", Joins: joins, Persistent: true)).Level;
            Assert.Equal([new Loc(12, 10)], level.FindFeature(TerrainFlags.UpStair));
            Assert.Equal([new Loc(150, 50)], level.FindFeature(TerrainFlags.DownStair));
            // Each in its own little staircase room (Angband build_staircase), reached by the tunnels.
            Assert.True(level[new Loc(12, 10)].Has(Angband.Core.World.SquareFlags.Room));
            Assert.True(level[new Loc(150, 50)].Has(Angband.Core.World.SquareFlags.Room));
        }
        // Angband labyrinth_gen, gauntlet_gen, hard_centre_gen: never in a persistent dungeon.
        for (ulong seed = 1; seed <= 60; seed++)
            Assert.DoesNotContain(generator.Generate(new LevelRequest(39, seed, Persistent: true)).Level.ProfileId,
                new[] { "labyrinth", "gauntlet", "hard_centre" });
    }
}
