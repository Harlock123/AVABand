namespace Angband.Core.Geometry;

/// <summary>A grid location. X is the column, Y is the row.</summary>
public readonly record struct Loc(int X, int Y)
{
    public static readonly Loc Zero = new(0, 0);

    public static Loc operator +(Loc a, Loc b) => new(a.X + b.X, a.Y + b.Y);
    public static Loc operator -(Loc a, Loc b) => new(a.X - b.X, a.Y - b.Y);

    public Loc Step(Direction dir) => this + dir.Offset();

    /// <summary>Angband's approximate Euclidean distance: max + min/2.</summary>
    public int DistanceTo(Loc other)
    {
        var dx = Math.Abs(X - other.X);
        var dy = Math.Abs(Y - other.Y);
        return dy > dx ? dy + (dx >> 1) : dx + (dy >> 1);
    }

    /// <summary>Number of king moves between two locations.</summary>
    public int ChebyshevTo(Loc other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public override string ToString() => $"({X},{Y})";
}
