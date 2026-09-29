using Angband.Core.Geometry;
using Angband.Input;

namespace Angband.Tests;

/// <summary>Two arrow keys held together move diagonally (for keyboards without a keypad).</summary>
public class ArrowChordTests
{
    private static (ArrowChord Chord, List<Direction> Moves) Make()
    {
        var chord = new ArrowChord();
        var moves = new List<Direction>();
        chord.Move += moves.Add;
        return (chord, moves);
    }

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void A_tap_moves_straight_as_it_is_let_go()
    {
        var (chord, moves) = Make();
        chord.Down(Direction.North, Ms(0));
        Assert.Empty(moves);
        chord.Up(Direction.North, Ms(20));
        Assert.Equal([Direction.North], moves);
    }

    [Fact]
    public void A_held_arrow_moves_after_the_grace_then_repeats()
    {
        var (chord, moves) = Make();
        chord.Down(Direction.West, Ms(0));
        chord.Tick(Ms(30));
        Assert.Empty(moves);
        chord.Tick(Ms(60));
        Assert.Equal([Direction.West], moves);
        chord.Down(Direction.West, Ms(500)); // the key's own repeat
        chord.Up(Direction.West, Ms(600));
        Assert.Equal([Direction.West, Direction.West], moves);
    }

    [Fact]
    public void Two_together_move_diagonally_once_and_keep_on_while_held()
    {
        var (chord, moves) = Make();
        chord.Down(Direction.North, Ms(0));
        chord.Down(Direction.East, Ms(20));
        chord.Tick(Ms(100));
        Assert.Equal([Direction.NorthEast], moves);
        chord.Down(Direction.East, Ms(500)); // repeating
        Assert.Equal([Direction.NorthEast, Direction.NorthEast], moves);
        chord.Up(Direction.North, Ms(600));
        chord.Up(Direction.East, Ms(610));
        Assert.Equal(2, moves.Count); // letting go adds nothing
    }

    [Fact]
    public void Opposite_arrows_make_no_diagonal()
    {
        var (chord, moves) = Make();
        chord.Down(Direction.North, Ms(0));
        chord.Down(Direction.South, Ms(10));
        chord.Tick(Ms(100));
        Assert.Empty(moves); // (neither moves while both are held)
    }
}
