using Angband.Core.Combat;
using Angband.Core.Geometry;

namespace Angband.Tests;

public class ProjectionPathTests
{
    private static readonly string[] Open =
    [
        "###############",
        "#,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,#",
        "###############",
    ];

    [Fact]
    public void StraightLine_StopsAtTarget()
    {
        var level = TestLevels.FromAscii(out _, Open);
        var path = ProjectionPath.Compute(level, new Loc(1, 1), new Loc(5, 1), 20);
        Assert.Equal([new Loc(2, 1), new Loc(3, 1), new Loc(4, 1), new Loc(5, 1)], path);
    }

    [Fact]
    public void Through_ContinuesToTheWall_IncludingIt()
    {
        var level = TestLevels.FromAscii(out _, Open);
        var path = ProjectionPath.Compute(level, new Loc(1, 1), new Loc(5, 1), 20, PathFlags.Through);
        Assert.Equal(new Loc(14, 1), path[^1]);
        Assert.Equal(13, path.Count);
    }

    [Fact]
    public void Range_CountsDiagonalsAsOneAndAHalf()
    {
        var level = TestLevels.FromAscii(out _, Open);
        var path = ProjectionPath.Compute(level, new Loc(1, 1), new Loc(7, 7), 6, PathFlags.Through);
        Assert.Equal(4, path.Count); // 4 + 4/2 = 6
        Assert.Equal(new Loc(5, 5), path[^1]);
    }

    [Fact]
    public void SlantedLine_ReachesTarget()
    {
        var level = TestLevels.FromAscii(out _, Open);
        var path = ProjectionPath.Compute(level, new Loc(1, 1), new Loc(12, 5), 20);
        Assert.Equal(new Loc(12, 5), path[^1]);
        for (var i = 1; i < path.Count; i++) Assert.Equal(1, path[i - 1].ChebyshevTo(path[i]));
    }

    [Fact]
    public void StopAtCreature()
    {
        var level = TestLevels.FromAscii(out _, Open);
        var path = ProjectionPath.Compute(level, new Loc(1, 3), new Loc(12, 3), 20, PathFlags.StopAtCreature,
            p => p == new Loc(6, 3));
        Assert.Equal(new Loc(6, 3), path[^1]);
    }

    [Fact]
    public void Projectable_BlockedByWalls()
    {
        var level = TestLevels.FromAscii(out _,
            "#########",
            "#,,,#,,,#",
            "#########");
        Assert.True(ProjectionPath.Projectable(level, new Loc(1, 1), new Loc(3, 1), 20));
        Assert.False(ProjectionPath.Projectable(level, new Loc(1, 1), new Loc(6, 1), 20));
        Assert.False(ProjectionPath.Projectable(level, new Loc(1, 1), new Loc(3, 1), 1));
    }
}
