using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Input;

namespace Angband.Tests;

/// <summary>Running (Angband Shift+direction): player-path.c's run_init / run_test / run_step.</summary>
public class RunTests
{
    /// <summary>A mapped level (everything remembered, as after exploring it) with the player at '@'.</summary>
    private static GameSession Mapped(params string[] rows)
    {
        var game = Arena.Create(3, rows);
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

    [Fact]
    public void Run_FollowsACorridorRoundItsBend_ToTheDeadEnd()
    {
        var game = Mapped(
            "##########",
            "#@.....###",
            "######.###",
            "######.###",
            "######.###",
            "##########");
        Assert.True(game.Execute(new RunCommand(Direction.East)));
        Assert.Equal(new Loc(6, 4), game.Player.Position);
    }

    /// <summary>A run taken a step at a time (for the interface to draw) goes as the run in one go does.</summary>
    [Fact]
    public void Run_TakenAStepAtATime_EndsWhereTheWholeRunDoes()
    {
        string[] map =
        [
            "##########",
            "#@.....###",
            "######.###",
            "######.###",
            "######.###",
            "##########",
        ];
        var whole = Mapped(map);
        whole.Execute(new RunCommand(Direction.East));

        var stepped = Mapped(map);
        var path = new List<Loc>();
        stepped.Events.Subscribe<PlayerMovedEvent>(e => path.Add(e.To));
        Assert.True(stepped.Execute(new RunStartCommand(Direction.East)));
        Assert.True(stepped.IsRunning);
        Assert.Single(path);
        var steps = 1;
        while (stepped.Execute(new RunOnCommand())) steps++;
        Assert.False(stepped.IsRunning);
        Assert.Equal(whole.Player.Position, stepped.Player.Position);
        Assert.Equal(whole.GameTurn, stepped.GameTurn);
        Assert.Equal(8, steps); // five east, three south: one square a step
        Assert.Equal(path.Count, steps);
        Assert.False(stepped.Execute(new RunOnCommand())); // nothing more

        // Anything else ends a run under way.
        var stopped = Mapped(map);
        stopped.Execute(new RunStartCommand(Direction.East));
        stopped.Execute(new HoldCommand());
        Assert.False(stopped.IsRunning);
        Assert.False(stopped.Execute(new RunOnCommand()));
    }

    [Fact]
    public void Run_StopsWhereACorridorBranches()
    {
        var game = Mapped(
            "###########",
            "#@........#",
            "######.####",
            "######.####",
            "###########");
        game.Execute(new RunCommand(Direction.East));
        Assert.Equal(new Loc(6, 1), game.Player.Position); // at the junction, not past it
    }

    [Fact]
    public void Run_StopsBesideADoor()
    {
        var game = Mapped(
            "##########",
            "#@.......#",
            "####+#####",
            "##########");
        game.Execute(new RunCommand(Direction.East));
        Assert.Equal(new Loc(3, 1), game.Player.Position); // the door is about to come alongside
    }

    [Fact]
    public void Run_AcrossARoom_StopsAtTheFarWall()
    {
        var game = Mapped(
            "#########",
            "#,,,,,,,#",
            "#,@,,,,,#",
            "#,,,,,,,#",
            "#########");
        game.Execute(new RunCommand(Direction.East));
        Assert.Equal(new Loc(7, 2), game.Player.Position);
    }

    [Fact]
    public void Run_StopsShortOfAVisibleMonster()
    {
        var game = Mapped(
            "###########",
            "#,,,,,,,,,#",
            "#,@,,,,,,,#",
            "#,,,,,,,,,#",
            "###########");
        var eye = Arena.AddMonster(game, "floating_eye", new Loc(6, 2), awake: false);
        game.UpdateView();
        Assert.True(eye.IsVisible);
        game.Execute(new RunCommand(Direction.East));
        Assert.Equal(new Loc(4, 2), game.Player.Position); // it would be next to the eye after one more step
    }

    [Fact]
    public void Run_StopsWhenAMonsterComesIntoView()
    {
        // A long lit corridor; a floating eye waits out of sight round the corner at the end.
        var game = Mapped(
            "####################",
            "#@,,,,,,,,,,,,,,,,,#",
            "##################,#",
            "##################,#",
            "##################,#",
            "####################");
        var eye = Arena.AddMonster(game, "floating_eye", new Loc(18, 4), awake: false);
        game.UpdateView();
        Assert.False(eye.IsVisible);
        game.Execute(new RunCommand(Direction.East));
        Assert.True(eye.IsVisible, $"stopped at {game.Player.Position}");
        // It first comes into view from the corner, and the run stops there, well short of it.
        Assert.True(game.Player.Position.Y < 3, $"ran to {game.Player.Position}");
    }

    [Fact]
    public void Run_IntoAKnownWall_SaysSo_AndTakesNoTime()
    {
        var game = Mapped(
            "#####",
            "#@..#",
            "#####");
        var messages = Messages(game);
        var turn = game.GameTurn;
        Assert.False(game.Execute(new RunCommand(Direction.North)));
        Assert.Equal(turn, game.GameTurn);
        Assert.Contains("There is a wall in the way!", messages);
    }

    [Fact]
    public void Run_IsStoppedByADisturbance()
    {
        var game = Mapped(
            "##################",
            "#@...............#",
            "##################");
        // Getting hungry disturbs, as in Angband: set food just above Hungry so the run crosses it.
        game.Player.Food = game.Data.Constants.FoodHungry; // the next digestion makes the player hungry
        game.Execute(new RunCommand(Direction.East));
        Assert.True(game.Player.Position.X < 16, $"ran to {game.Player.Position}");
    }

    [Fact]
    public void RunKeys_AreShiftWithTheArrowsAndKeypad_TheRoguelikeCapitals_AndDot()
    {
        var keys = InputBindings.Defaults();
        Assert.Equal(InputAction.RunNorth, keys.ForKey("Shift+Up"));
        Assert.Equal(InputAction.RunSouthEast, keys.ForKey("Shift+NumPad3"));
        Assert.Equal(InputAction.RunWest, keys.ForKey("Char:H"));
        Assert.Equal(InputAction.Run, keys.ForKey("Char:."));
        Assert.Equal(InputAction.ToggleIgnore, keys.ForKey("Char:K")); // taken, as before
        Assert.Equal(Direction.NorthEast, InputAction.RunNorthEast.ToRunDirection());
        Assert.Null(InputAction.MoveNorth.ToRunDirection());
    }

    [Fact]
    public void RunKeys_AreAddedToOlderSavedBindings()
    {
        var old = InputBindings.Defaults();
        foreach (var key in old.Keys.Where(kv => kv.Value.ToRunDirection() is not null || kv.Value == InputAction.Run).Select(kv => kv.Key).ToList())
            old.Keys.Remove(key);
        old.AddMissingDefaults();
        Assert.Equal(InputAction.RunEast, old.ForKey("Shift+Right"));
        Assert.Equal(InputAction.Run, old.ForKey("Char:."));
    }
}
