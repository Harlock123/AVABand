using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>Builds small levels from pictures for vision tests.</summary>
internal static class TestLevels
{
    /// <summary>
    /// Legend: <c>#</c> granite, <c>g</c> glowing granite, <c>.</c> dark (corridor) floor, <c>,</c> glowing room floor,
    /// <c>+</c> closed door, <c>'</c> open door, <c>@</c> dark floor marking the returned eye position.
    /// </summary>
    public static Level FromAscii(out Loc eye, params string[] rows) => FromAscii(1, out eye, rows);

    /// <summary>The same, for a level at <paramref name="depth"/>.</summary>
    public static Level FromAscii(int depth, out Loc eye, params string[] rows)
    {
        var t = TestData.Game.Terrain;
        var level = new Level(t, rows[0].Length, rows.Length, depth);
        eye = default;
        for (var y = 0; y < rows.Length; y++)
        for (var x = 0; x < rows[y].Length; x++)
        {
            ref var sq = ref level[x, y];
            var ch = rows[y][x];
            sq.Feature = ch switch
            {
                '#' or 'g' => t.Ids.Granite,
                '+' => t.Ids.ClosedDoor,
                '\'' => t.Ids.OpenDoor,
                _ => t.Ids.Floor,
            };
            if (ch is 'g' or ',') sq.Flags |= SquareFlags.Glow | SquareFlags.Room;
            if (ch == '@') eye = new Loc(x, y);
        }
        return level;
    }

    /// <summary>Renders a visibility mask: <c>*</c> visible, <c>-</c> not.</summary>
    public static string[] Mask(Level level, Func<Loc, bool> visible) =>
        Enumerable.Range(0, level.Height)
            .Select(y => new string(Enumerable.Range(0, level.Width).Select(x => visible(new Loc(x, y)) ? '*' : '-').ToArray()))
            .ToArray();
}
