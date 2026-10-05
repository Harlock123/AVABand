using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>Auto-explore (AVABand's own; Ctrl+E).</summary>
public class ExploreTests
{
    private static (GameSession Game, List<string> Said) OnLevel(ulong seed, int depth = 2)
    {
        var game = GameSession.NewGame(TestData.Game, seed, "warrior");
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(depth));
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        return (game, said);
    }

    private static int KnownSquares(GameSession game) => game.Level.AllLocs().Count(game.Known.IsKnown);

    [Theory]
    [InlineData(1UL)]
    [InlineData(5UL)]
    public void Exploring_maps_the_level_until_nothing_is_left(ulong seed)
    {
        var (game, said) = OnLevel(seed);
        var start = KnownSquares(game);
        var presses = 0;
        while (presses++ < 300 && !said.Any(s => s.Contains("nothing left", StringComparison.Ordinal) || s.StartsWith("You have seen all", StringComparison.Ordinal)))
        {
            TestGames.ClearMonsters(game);                                       // (nothing to interrupt it)
            game.Execute(new ExploreCommand());
        }
        Assert.True(presses < 300, "explore never finished");
        Assert.True(KnownSquares(game) > start * 3, $"{start} → {KnownSquares(game)}");
        Assert.Null(game.NearestUnexplored());                                   // no reachable edge left
    }

    [Fact]
    public void A_monster_coming_into_view_stops_it()
    {
        var (game, _) = OnLevel(1);
        TestGames.ClearMonsters(game);
        var turn = game.GameTurn;
        // A monster waiting where the explore will go.
        var edge = game.NearestUnexplored()!.Value;
        var spot = game.Level.AllLocs().Where(p => game.Level.IsEmptyFloor(p) && !game.Known.IsKnown(p)).OrderBy(p => p.DistanceTo(edge)).First();
        Arena.AddMonster(game, "cave_spider", spot, awake: false);
        game.Execute(new ExploreCommand());
        Assert.True(game.GameTurn > turn);
        Assert.Contains(game.Level.Monsters.All, m => m.IsVisible);              // stopped on seeing it
    }

    [Fact]
    public void Nothing_left_says_so_and_takes_no_time()
    {
        var game = Arena.Create(1,
            "#####",
            "#@,,#",
            "#####");
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.Execute(new ExploreCommand());                                     // (a fresh game's first command readies the player)
        var turn = game.GameTurn;
        var at = game.Player.Position;
        game.Execute(new ExploreCommand());
        Assert.Equal(turn, game.GameTurn);
        Assert.Equal(at, game.Player.Position);
        Assert.Contains("There's nothing left here to explore that you can reach.", said);
    }
}
