namespace Angband.Core.Geometry;

/// <summary>An axis-aligned rectangle with <b>inclusive</b> corners, matching Angband's room code.</summary>
public readonly record struct Rect(int X1, int Y1, int X2, int Y2)
{
    public static Rect FromSize(int x, int y, int width, int height) => new(x, y, x + width - 1, y + height - 1);

    public int Width => X2 - X1 + 1;
    public int Height => Y2 - Y1 + 1;
    public Loc Center => new((X1 + X2) / 2, (Y1 + Y2) / 2);

    public bool Contains(Loc p) => p.X >= X1 && p.X <= X2 && p.Y >= Y1 && p.Y <= Y2;

    public Rect Inflate(int amount) => new(X1 - amount, Y1 - amount, X2 + amount, Y2 + amount);

    public bool IsOnEdge(Loc p) => Contains(p) && (p.X == X1 || p.X == X2 || p.Y == Y1 || p.Y == Y2);

    public IEnumerable<Loc> Cells()
    {
        for (var y = Y1; y <= Y2; y++)
        for (var x = X1; x <= X2; x++)
            yield return new Loc(x, y);
    }

    public IEnumerable<Loc> Edge()
    {
        for (var x = X1; x <= X2; x++)
        {
            yield return new Loc(x, Y1);
            if (Y2 != Y1) yield return new Loc(x, Y2);
        }
        for (var y = Y1 + 1; y < Y2; y++)
        {
            yield return new Loc(X1, y);
            if (X2 != X1) yield return new Loc(X2, y);
        }
    }
}
