using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

public class GameSessionTests
{
    [Fact]
    public void NewGame_StartsInTownOnPassableGround()
    {
        var game = GameSession.NewGame(TestData.Game, 1);
        Assert.Equal(0, game.Player.Depth);
        Assert.True(game.Level.IsPassable(game.Player.Position));
        Assert.True(game.Player.Energy >= 100);
    }

    [Fact]
    public void Walking_CostsOneNormalTurn()
    {
        var game = GameSession.NewGame(TestData.Game, 1);
        var dir = DirectionExtensions.Compass.First(d => game.Level.IsPassable(game.Player.Position.Step(d)));
        var start = game.Player.Position;
        var turn = game.GameTurn;

        Assert.True(game.Execute(new WalkCommand(dir)));

        Assert.Equal(start.Step(dir), game.Player.Position);
        Assert.Equal(turn + 10, game.GameTurn);
    }

    [Fact]
    public void WalkingIntoWall_TakesNoTime()
    {
        var game = GameSession.NewGame(TestData.Game, 1);
        var level = game.Level;
        // Stand next to the town's permanent border and walk into it.
        var spot = level.AllLocs().First(p => p.Y == 1 && level.IsPassable(p));
        game.Player.Position = spot;
        var turn = game.GameTurn;

        Assert.False(game.Execute(new WalkCommand(Direction.North)));
        Assert.Equal(turn, game.GameTurn);
    }

    [Fact]
    public void TakingStairs_ChangesDepth()
    {
        var game = GameSession.NewGame(TestData.Game, 7);
        game.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();

        Assert.True(game.Execute(new TakeStairsCommand(Down: true)));

        Assert.Equal(1, game.Player.Depth);
        Assert.True(game.Level.Has(game.Player.Position, TerrainFlags.UpStair)); // connected stairs: a way back
    }

    [Fact]
    public void SameSeedAndCommands_ReplayIdentically()
    {
        static string Play()
        {
            var game = GameSession.NewGame(TestData.Game, 2025);
            game.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
            game.Execute(new TakeStairsCommand(true));
            game.Execute(new TakeStairsCommand(true));
            foreach (var dir in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
                game.Execute(new WalkCommand(dir));
            return $"{game.Player.Position} {game.GameTurn}\n{game.Level.ToAscii()}";
        }

        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Events_ArePublished()
    {
        var game = GameSession.NewGame(TestData.Game, 3);
        var events = new List<IGameEvent>();
        using var _ = game.Events.SubscribeAll(events.Add);

        game.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Execute(new TakeStairsCommand(true));

        Assert.Contains(events, e => e is StairsTakenEvent { Down: true });
        Assert.Contains(events, e => e is LevelChangedEvent { Depth: 1 });
    }
}
