using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2's walk and jump (cmd-cave.c do_cmd_walk / do_cmd_jump): walking at a trap you know of
/// disarms it (trying again and again); walking into a trap ('W' / '-') steps onto it.
/// </summary>
public class WalkIntoTrapTests
{
    private static (GameSession Game, Loc Trap) Game()
    {
        var game = Arena.Create(4, "#######", "#,,,,,#", "#,@,,,#", "#,,,,,#", "#######");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 10_000;
        var at = game.Player.Position + new Loc(1, 0);
        game.Level[at].Trap = game.Data.Traps.Single(t => t.Id == "pit").Index;
        game.Level[at].Flags |= SquareFlags.TrapVisible;
        return (game, at);
    }

    [Fact]
    public void WalkingAtAKnownTrap_DisarmsIt_WithoutSteppingOn()
    {
        var (game, trap) = Game();
        game.Player.DisarmSkill = 1000; // sure hands
        var start = game.Player.Position;
        Assert.True(game.Execute(new WalkCommand(Direction.East)));
        Assert.Equal(start, game.Player.Position);
        Assert.Equal(0, game.Level[trap].Trap);
    }

    [Fact]
    public void WalkingIntoATrap_StepsOnIt()
    {
        var (game, trap) = Game();
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        Assert.True(game.Execute(new JumpCommand(Direction.East)));
        Assert.Equal(trap, game.Player.Position);
        Assert.Contains("You set off a pit!", said);
    }
}
