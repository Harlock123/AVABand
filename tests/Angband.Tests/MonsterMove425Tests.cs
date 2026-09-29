using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>Angband 4.2.5's noise and scent (game-world.c make_noise, update_scent) that monsters track by.</summary>
public class MonsterMove425Tests
{
    private static readonly string[] Bend =
    [
        "#########",
        "#@,,,,,,#",
        "#######,#",
        "#######,#",
        "#########",
    ];

    [Fact]
    public void Noise_flows_round_corners_one_step_at_a_time_and_four_while_covering_tracks()
    {
        var game = Arena.Create(1, Bend);
        game.Execute(new HoldCommand());
        Assert.Equal(0, game.Noise[new Loc(1, 1)]);   // the player's own square is silent
        Assert.Equal(6, game.Noise[new Loc(7, 2)]);
        Assert.Equal(7, game.Noise[new Loc(7, 3)]);   // round the bend, not through the rock
        Assert.Equal(0, game.Noise[new Loc(4, 3)]);   // rock carries no noise

        game.Player.Timed.Set(game.Data.Timed("covertracks")!, 50);
        game.Execute(new HoldCommand());
        Assert.Equal(28, game.Noise[new Loc(7, 3)]);
    }

    [Fact]
    public void Scent_ages_each_turn_and_none_is_laid_while_covering_tracks()
    {
        var game = Arena.Create(1, Bend);
        game.Execute(new HoldCommand());
        var fresh = game.Scent[new Loc(2, 1)];
        Assert.InRange(fresh, 1, 2);
        Assert.Equal(0, game.Scent[new Loc(1, 1)]);   // none on the player's square: no one smells it

        game.Player.Timed.Set(game.Data.Timed("covertracks")!, 50);
        var turn = game.GameTurn;
        game.Execute(new HoldCommand());
        var worldTurns = (int)((game.GameTurn - turn) / 10);
        Assert.True(worldTurns > 0);
        Assert.Equal(fresh + worldTurns, game.Scent[new Loc(2, 1)]);
    }

    [Fact]
    public void Scent_spreads_over_the_floor_not_through_walls()
    {
        var game = Arena.Create(1, Bend);
        game.Player.Position = new Loc(6, 1);
        game.UpdateView();
        game.Execute(new HoldCommand());
        Assert.Equal(1, game.Scent[new Loc(7, 2)]);   // next to the player
        Assert.Equal(2, game.Scent[new Loc(7, 3)]);   // two away, beside a fresher square
        Assert.Equal(0, game.Scent[new Loc(5, 2)]);   // walls carry none (NO_SCENT)
        // update_scent lays the 5×5 in reading order, so a square two away only takes scent from a
        // fresher neighbour already laid: west of the player, in its own row, nothing yet.
        Assert.Equal(0, game.Scent[new Loc(4, 1)]);
    }
}
