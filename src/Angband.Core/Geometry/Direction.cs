namespace Angband.Core.Geometry;

/// <summary>Directions numbered like the numeric keypad, as in Angband.</summary>
public enum Direction
{
    None = 0,
    SouthWest = 1,
    South = 2,
    SouthEast = 3,
    West = 4,
    Here = 5,
    East = 6,
    NorthWest = 7,
    North = 8,
    NorthEast = 9,
}

public static class DirectionExtensions
{
    /// <summary>The eight compass directions in a fixed order.</summary>
    public static readonly Direction[] Compass =
    [
        Direction.North, Direction.NorthEast, Direction.East, Direction.SouthEast,
        Direction.South, Direction.SouthWest, Direction.West, Direction.NorthWest,
    ];

    /// <summary>The four orthogonal directions.</summary>
    public static readonly Direction[] Orthogonal =
        [Direction.North, Direction.East, Direction.South, Direction.West];

    public static Loc Offset(this Direction dir) => dir switch
    {
        Direction.SouthWest => new Loc(-1, 1),
        Direction.South => new Loc(0, 1),
        Direction.SouthEast => new Loc(1, 1),
        Direction.West => new Loc(-1, 0),
        Direction.East => new Loc(1, 0),
        Direction.NorthWest => new Loc(-1, -1),
        Direction.North => new Loc(0, -1),
        Direction.NorthEast => new Loc(1, -1),
        _ => Loc.Zero,
    };

    public static Direction FromOffset(int dx, int dy) => (Math.Sign(dx), Math.Sign(dy)) switch
    {
        (-1, 1) => Direction.SouthWest,
        (0, 1) => Direction.South,
        (1, 1) => Direction.SouthEast,
        (-1, 0) => Direction.West,
        (1, 0) => Direction.East,
        (-1, -1) => Direction.NorthWest,
        (0, -1) => Direction.North,
        (1, -1) => Direction.NorthEast,
        _ => Direction.Here,
    };
}
