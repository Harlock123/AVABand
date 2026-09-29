using Angband.Core.Game;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>AVABand's option for keyboards without a keypad: levels that never need a diagonal step.</summary>
public class DiagonalSqueezeTests
{
    private static readonly DungeonGenerator Generator = new(TestData.Game);

    /// <summary>Walkable squares that can't be reached from the start with orthogonal steps (vaults aside).</summary>
    private static List<Loc> NeedDiagonals(Level level, Loc start)
    {
        var seen = new bool[level.Width * level.Height];
        var queue = new Queue<Loc>([start]);
        seen[start.Y * level.Width + start.X] = true;
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in DirectionExtensions.Orthogonal)
            {
                var n = p.Step(d);
                if (!level.InBounds(n) || seen[n.Y * level.Width + n.X] || !level.IsTraversable(n)) continue;
                seen[n.Y * level.Width + n.X] = true;
                queue.Enqueue(n);
            }
        }
        return level.AllLocs().Where(p => level.IsTraversable(p) && !seen[p.Y * level.Width + p.X]
                                          && !level[p].Has(SquareFlags.Vault)).ToList();
    }

    [Fact]
    public void Two_rooms_touching_at_a_corner_are_joined()
    {
        var level = TestLevels.FromAscii(out var eye,
            "########",
            "#@.#####",
            "#..#####",
            "###...##",
            "###...##",
            "########");
        Assert.NotEmpty(NeedDiagonals(level, eye));
        Assert.Equal(1, Connectivity.OpenDiagonalSqueezes(level));
        Assert.Empty(NeedDiagonals(level, eye));
    }

    [Theory]
    [InlineData("classic", 5)]
    [InlineData("modified", 25)]
    [InlineData("moria", 20)]
    [InlineData("cavern", 30)]
    [InlineData("labyrinth", 15)]
    [InlineData("lair", 40)]
    [InlineData("gauntlet", 40)]
    [InlineData("hard_centre", 60)]
    [InlineData("town", 0)]
    public void With_the_option_every_level_kind_can_be_walked_with_four_directions(string profile, int depth)
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var g = Generator.Generate(new LevelRequest(depth, seed, ProfileId: profile, NoDiagonalSqueezes: true));
            var stuck = NeedDiagonals(g.Level, g.PlayerStart);
            Assert.True(stuck.Count == 0, $"{profile} seed {seed}: {stuck.Count} squares need a diagonal (first {stuck.FirstOrDefault()})");
        }
    }

    [Fact]
    public void Without_the_option_levels_are_generated_as_before()
    {
        // This one has a corridor that meets a room only at a corner.
        var plain = Generator.Generate(new LevelRequest(25, 3, ProfileId: "modified")).Level;
        var again = Generator.Generate(new LevelRequest(25, 3, ProfileId: "modified")).Level;
        var opened = Generator.Generate(new LevelRequest(25, 3, ProfileId: "modified", NoDiagonalSqueezes: true)).Level;
        Assert.True(plain.AllLocs().All(p => plain[p].Feature == again[p].Feature));
        var changed = plain.AllLocs().Where(p => plain[p].Feature != opened[p].Feature).ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, p => Assert.True(opened.IsFloor(p))); // only walls opened into floor
    }

    [Fact]
    public void The_game_uses_the_option_for_new_levels()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        game.MarkDebugUsed();
        game.ApplyInterfaceOptions(new Dictionary<string, bool> { [OptionIds.NoDiagonalSqueezes] = true });
        for (var depth = 15; depth <= 30; depth += 5)
        {
            game.Execute(new DebugJumpCommand(depth));
            Assert.Empty(NeedDiagonals(game.Level, game.Player.Position));
        }
    }
}
