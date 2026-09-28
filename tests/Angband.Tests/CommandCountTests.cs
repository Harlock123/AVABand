using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>
/// Command counts (Angband 4.2 cmd-core.c): '0' and a number before walking, holding, tunnelling,
/// opening or disarming; opening and disarming also repeat by themselves, up to 99 tries.
/// </summary>
public class CommandCountTests
{
    private static GameSession Game(params string[] rows)
    {
        var game = Arena.Create(3, rows);
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 10_000;
        game.Known.RememberAll(game.Level);
        game.UpdateView();
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    private static long OneTurn()
    {
        var game = Game("#####", "#,@,#", "#####");
        var turn = game.GameTurn;
        game.Execute(new HoldCommand());
        return game.GameTurn - turn;
    }

    [Fact]
    public void Walking_WithACount_TakesThatManySteps_OrStopsAtAWall()
    {
        var game = Game("##########", "#@,,,,,,,#", "##########");
        var start = game.Player.Position;
        Assert.True(game.Execute(new CountedCommand(new WalkCommand(Direction.East), 3)));
        Assert.Equal(start + new Loc(3, 0), game.Player.Position);

        game.Execute(new CountedCommand(new WalkCommand(Direction.East), 20));
        Assert.Equal(new Loc(8, 1), game.Player.Position);
    }

    [Fact]
    public void Holding_WithACount_PassesThatManyTurns()
    {
        var game = Game("#####", "#,@,#", "#####");
        var turn = game.GameTurn;
        game.Execute(new CountedCommand(new HoldCommand(), 5));
        Assert.Equal(5 * OneTurn(), game.GameTurn - turn);
    }

    [Fact]
    public void AMonsterComingIntoView_StopsTheRepetition()
    {
        var game = Game("#########", "#,@,,,,,#", "#########");
        game.Execute(new HoldCommand());
        // A jackal turns up: not yet seen when the count starts, seen after its first turn.
        var jackal = Arena.AddMonster(game, "jackal", new Loc(6, 1));
        jackal.Held = 1000;
        jackal.IsVisible = false;
        var turn = game.GameTurn;
        game.Execute(new CountedCommand(new HoldCommand(), 50));
        var turns = (game.GameTurn - turn) / OneTurn();
        Assert.Equal(1, turns); // it came into view during the first counted turn
    }

    [Fact]
    public void Opening_ALockedDoor_KeepsTryingUpToTheCount()
    {
        var game = Game("#####", "#,@+#", "#####");
        var door = new Loc(3, 1);
        game.Level[door].LockPower = 30;
        game.Player.DisarmSkill = 0; // the least chance there is (2%)
        var said = Messages(game);
        game.Execute(new CountedCommand(new OpenCommand(Direction.East), 5));
        var tries = said.Count(m => m == "You failed to pick the lock.") + said.Count(m => m == "You have picked the lock.");
        Assert.Equal(said.Contains("You have picked the lock.") ? tries : 5, tries);
        Assert.InRange(tries, 1, 5);
    }

    [Fact]
    public void Opening_WithoutACount_TriesUpTo99Times()
    {
        var game = Game("#####", "#,@+#", "#####");
        var door = new Loc(3, 1);
        game.Level[door].LockPower = 30;
        game.Player.DisarmSkill = 0;
        var said = Messages(game);
        game.Execute(new OpenCommand(Direction.East));
        var failures = said.Count(m => m == "You failed to pick the lock.");
        Assert.True(failures > 1, "only one try");
        Assert.True(failures == 99 || said.Contains("You have picked the lock."));
    }

    [Fact]
    public void Tunnelling_WithACount_DigsNoMoreThanThat()
    {
        var game = Game("#####", "#,@##", "#####");
        game.Level[new Loc(3, 1)].Feature = game.Data.Terrain.Ids.Granite;
        var turn = game.GameTurn;
        game.Execute(new CountedCommand(new TunnelCommand(Direction.East), 4));
        Assert.True(game.GameTurn - turn <= 4 * OneTurn());
    }

    [Theory]
    [InlineData(typeof(WalkCommand), true)]
    [InlineData(typeof(TunnelCommand), true)]
    [InlineData(typeof(CastCommand), false)]
    public void OnlySomeCommandsTakeACount(Type type, bool takes)
    {
        GameCommand command = type == typeof(WalkCommand) ? new WalkCommand(Direction.East)
            : type == typeof(TunnelCommand) ? new TunnelCommand(Direction.East) : new CastCommand("magic_missile");
        Assert.Equal(takes, GameSession.TakesCount(command));
    }
}
