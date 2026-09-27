using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Sight;

namespace Angband.Tests;

public class FovTests
{
    private static Func<Loc, bool> Visible(Angband.Core.World.Level level, Loc eye, int radius = 20)
    {
        var mask = Fov.VisibleMask(level, eye, radius);
        return p => mask[p.Y * level.Width + p.X];
    }

    [Fact]
    public void OpenRoom_EverythingVisible()
    {
        var level = TestLevels.FromAscii(out var eye,
            "#######",
            "#.....#",
            "#..@..#",
            "#.....#",
            "#######");
        Assert.All(level.AllLocs(), p => Assert.True(Visible(level, eye)(p), $"{p}"));
    }

    [Fact]
    public void Pillar_CastsShadow()
    {
        var level = TestLevels.FromAscii(out var eye,
            "###########",
            "#@..#.....#",
            "###########");
        var mask = TestLevels.Mask(level, Visible(level, eye));
        Assert.Equal("*****------", mask[1]); // the pillar itself is seen, nothing behind it
    }

    [Fact]
    public void Shadow_MatchesExpectedShape()
    {
        var level = TestLevels.FromAscii(out var eye,
            "#########",
            "#.......#",
            "#.......#",
            "#...#...#",
            "#.......#",
            "#...@...#",
            "#########");
        var mask = TestLevels.Mask(level, Visible(level, eye));
        Assert.Equal(
        [
            "****-****",
            "****-****",
            "****-****",
            "*********",
            "*********",
            "*********",
            "*********",
        ], mask);
    }

    [Fact]
    public void ClosedDoor_BlocksSight_OpenDoorDoesNot()
    {
        var closed = TestLevels.FromAscii(out var eye1, "#########", "#@..+...#", "#########");
        Assert.Equal("*****----", TestLevels.Mask(closed, Visible(closed, eye1))[1]);

        var open = TestLevels.FromAscii(out var eye2, "#########", "#@..'...#", "#########");
        Assert.Equal("*********", TestLevels.Mask(open, Visible(open, eye2))[1]);
    }

    [Fact]
    public void CanSeeAroundOutsideCornerOnlyAlongSymmetricLines()
    {
        // Looking down a corridor that opens into a room: the room is partly visible.
        var level = TestLevels.FromAscii(out var eye,
            "###########",
            "#.........#",
            "#.........#",
            "#####.#####",
            "#####.#####",
            "#####@#####",
            "###########");
        var v = Visible(level, eye);
        Assert.True(v(new Loc(5, 1)));
        Assert.True(v(new Loc(4, 1)));
        Assert.False(v(new Loc(1, 2)));
    }

    [Fact]
    public void Radius_UsesAngbandDistance()
    {
        var rows = new[] { new string('#', 31) }
            .Concat(Enumerable.Range(0, 29).Select(i => "#" + new string(i == 14 ? '.' : '.', 29) + "#"))
            .Concat([new string('#', 31)]).ToArray();
        var level = TestLevels.FromAscii(out _, rows);
        var eye = new Loc(15, 15);
        var v = Visible(level, eye, radius: 5);
        foreach (var p in level.AllLocs())
            Assert.Equal(eye.DistanceTo(p) <= 5, v(p));
    }

    [Fact]
    public void ZeroRadius_SeesOnlyOrigin()
    {
        var level = TestLevels.FromAscii(out var eye, "#####", "#.@.#", "#####");
        var v = Visible(level, eye, radius: 0);
        Assert.Equal([eye], level.AllLocs().Where(v));
    }

    [Fact]
    public void OriginAtEdge_DoesNotThrow()
    {
        var level = TestLevels.FromAscii(out _, "....", "....", "....");
        Fov.Compute(level, new Loc(0, 0), 20, _ => { });
        Fov.Compute(level, new Loc(3, 2), 20, _ => { });
    }

    [Theory]
    [InlineData(5UL, "classic", 10)]
    [InlineData(6UL, "cavern", 20)]
    [InlineData(7UL, "labyrinth", 10)]
    public void Fov_IsSymmetricBetweenOpenSquares(ulong seed, string profile, int depth)
    {
        var level = new DungeonGenerator(TestData.Game).Generate(new LevelRequest(depth, seed, ProfileId: profile)).Level;
        var rng = new Angband.Core.Randomness.GameRandom(seed);
        var open = level.AllLocs().Where(p => !Fov.BlocksSight(level, p)).ToList();
        const int radius = 20;

        for (var i = 0; i < 60; i++)
        {
            var a = rng.Pick(open);
            var fromA = Fov.VisibleMask(level, a, radius);
            foreach (var b in open)
            {
                if (a.DistanceTo(b) > radius) continue;
                var aSeesB = fromA[b.Y * level.Width + b.X];
                if (!aSeesB) continue;
                var bSeesA = Fov.VisibleMask(level, b, radius)[a.Y * level.Width + a.X];
                Assert.True(bSeesA, $"{a} sees {b} but not vice versa ({profile}, seed {seed})");
            }
        }
    }
}
