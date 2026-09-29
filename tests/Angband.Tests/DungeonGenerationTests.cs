using Angband.Core.Definitions;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Tests;

public class DungeonGenerationTests
{
    private static readonly DungeonGenerator Generator = new(TestData.Game);

    public static TheoryData<int, ulong> DepthsAndSeeds()
    {
        var data = new TheoryData<int, ulong>();
        foreach (var depth in new[] { 1, 3, 8, 15, 25, 40, 60, 99, 127 })
        foreach (var seed in new ulong[] { 1, 2, 3, 1234, 987_654_321 })
            data.Add(depth, seed);
        return data;
    }

    [Theory]
    [MemberData(nameof(DepthsAndSeeds))]
    public void GeneratedLevels_AreValid(int depth, ulong seed)
    {
        var result = Generator.Generate(new LevelRequest(depth, seed));
        AssertValid(result);
        Assert.Equal(depth, result.Level.Depth);
    }

    public static TheoryData<string, int> Profiles() => new()
    {
        { "classic", 1 }, { "classic", 45 }, { "modified", 5 }, { "modified", 50 }, { "moria", 20 }, { "lair", 30 },
        { "gauntlet", 40 }, { "hard_centre", 60 }, { "cavern", 20 }, { "cavern", 80 }, { "labyrinth", 5 }, { "labyrinth", 70 },
    };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void EveryProfile_ProducesValidLevels(string profile, int depth)
    {
        for (ulong seed = 100; seed < 115; seed++)
        {
            var result = Generator.Generate(new LevelRequest(depth, seed, ProfileId: profile));
            Assert.Equal(profile, result.Level.ProfileId);
            AssertValid(result);
        }
    }

    [Fact]
    public void SameRequest_GivesIdenticalLevel()
    {
        var request = new LevelRequest(20, 424242, StairArrival.Descended);
        var a = Generator.Generate(request);
        var b = Generator.Generate(request);

        Assert.Equal(a.Level.ToAscii(), b.Level.ToAscii());
        Assert.Equal(a.PlayerStart, b.PlayerStart);
        Assert.Equal(a.Level.SpawnHints, b.Level.SpawnHints);
        Assert.True(a.Level.Squares.SequenceEqual(b.Level.Squares, SquareComparer.Instance));
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentLevels()
    {
        var a = Generator.Generate(new LevelRequest(10, 1)).Level.ToAscii();
        var b = Generator.Generate(new LevelRequest(10, 2)).Level.ToAscii();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ClassicLevels_HaveRoomsCorridorsAndDoors()
    {
        var level = Generator.Generate(new LevelRequest(5, 77, ProfileId: "classic")).Level;
        var locs = level.AllLocs().ToList();

        Assert.Equal(198, level.Width);
        Assert.Equal(66, level.Height);
        Assert.Contains(locs, p => level[p].Has(SquareFlags.Room) && level.IsFloor(p));
        Assert.Contains(locs, p => !level[p].Has(SquareFlags.Room) && level.IsFloor(p));
        Assert.Contains(locs, p => level.IsDoor(p));
    }

    [Fact]
    public void ConnectedStairs_PutPlayerOnAWayBack()
    {
        // Angband 4.2 (gen-util.c): going down leaves you on an up staircase, and vice versa.
        var down = Generator.Generate(new LevelRequest(10, 5, StairArrival.Descended));
        Assert.True(down.Level.Has(down.PlayerStart, TerrainFlags.UpStair));

        var up = Generator.Generate(new LevelRequest(10, 5, StairArrival.Ascended));
        Assert.True(up.Level.Has(up.PlayerStart, TerrainFlags.DownStair));

        var unconnected = Generator.Generate(new LevelRequest(10, 5, StairArrival.Descended, ConnectStairs: false));
        Assert.True(unconnected.Level.IsEmptyFloor(unconnected.PlayerStart));

        var fresh = Generator.Generate(new LevelRequest(10, 5));
        Assert.True(fresh.Level.IsEmptyFloor(fresh.PlayerStart));
    }

    [Fact]
    public void BottomLevel_HasNoDownStairsUnderArrivingPlayer()
    {
        var max = TestData.Game.Constants.MaxDepth;
        var result = Generator.Generate(new LevelRequest(max, 9, StairArrival.Descended));
        Assert.True(result.Level.Has(result.PlayerStart, TerrainFlags.UpStair));
        Assert.DoesNotContain(result.Level.AllLocs(), p =>
            result.Level[p].Trap != 0 && TestData.Game.TrapByIndex(result.Level[p].Trap)!.IsTrapDoor);
    }

    [Fact]
    public void Traps_RespectDepth()
    {
        for (ulong seed = 0; seed < 10; seed++)
        {
            var level = Generator.Generate(new LevelRequest(1, seed)).Level;
            foreach (var p in level.AllLocs())
            {
                if (level[p].Trap == 0) continue;
                var trap = TestData.Game.TrapByIndex(level[p].Trap)!;
                Assert.True(trap.MinDepth <= 1, $"{trap.Id} appeared at depth 1");
                Assert.True(level.IsFloor(p));
            }
        }
    }

    [Fact]
    public void Levels_AreGivenTheirMonstersAndObjects()
    {
        // Angband classic_gen: 14 + 1d8 + k random monsters (sleeping, with their groups), and
        // objects in rooms and anywhere, all planned at generation.
        var level = Generator.Generate(new LevelRequest(30, 3, ProfileId: "classic")).Level;
        Assert.True(level.SpawnHints.Count(h => h.Kind == SpawnKind.Monster) >= 14);
        Assert.Contains(level.SpawnHints, h => h.Kind == SpawnKind.Object);
        Assert.Contains(level.SpawnHints, h => h.Kind == SpawnKind.Gold);
    }

    [Fact]
    public void DeepLevels_EventuallyContainVaults()
    {
        var found = false;
        for (ulong seed = 0; seed < 30 && !found; seed++)
        {
            var level = Generator.Generate(new LevelRequest(50, seed, ProfileId: "modified")).Level;
            found = level.AllLocs().Any(p => level[p].Has(SquareFlags.Vault));
        }
        Assert.True(found, "no vault in 30 modified levels at depth 50");
    }

    [Fact]
    public void ProfileSelection_RespectsMinimumDepth()
    {
        for (ulong seed = 0; seed < 40; seed++)
        {
            // Angband choose_profile: classic and modified from the start; caverns from 15, lairs and
            // gauntlets from 20, hard centres from 50; labyrinths from 13; moria levels 10-39.
            var level = Generator.Generate(new LevelRequest(2, seed)).Level;
            Assert.Contains(level.ProfileId, new[] { "classic", "modified" });
        }
    }

    internal static void AssertValid(GeneratedLevel result)
    {
        var errors = LevelValidator.Validate(result.Level, result.PlayerStart, TestData.Game.Constants);
        Assert.True(errors.Count == 0,
            $"profile {result.Level.ProfileId}, depth {result.Level.Depth}, seed {result.Level.Seed}:\n" +
            string.Join("\n", errors) + "\n" + result.Level.ToAscii());
    }

    private sealed class SquareComparer : IEqualityComparer<Square>
    {
        public static readonly SquareComparer Instance = new();
        public bool Equals(Square a, Square b) =>
            a.Feature == b.Feature && a.Flags == b.Flags && a.Trap == b.Trap && a.LockPower == b.LockPower;
        public int GetHashCode(Square s) => HashCode.Combine(s.Feature, s.Flags, s.Trap, s.LockPower);
    }
}
