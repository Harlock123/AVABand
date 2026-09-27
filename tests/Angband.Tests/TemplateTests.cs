using Angband.Core.Definitions;
using Angband.Core.Generation;

namespace Angband.Tests;

public class TemplateTests
{
    public static TheoryData<string> TemplateIds()
    {
        var data = new TheoryData<string>();
        foreach (var t in TestData.Game.Templates) data.Add(t.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Template_IsInternallyConnected(string id)
    {
        // Walking or tunnelling: vault pockets may be sealed by granite, never by permanent rock.
        var rows = TestData.Game.Templates.Single(t => t.Id == id).Rows;
        var cells = new List<(int X, int Y)>();
        for (var y = 0; y < rows.Count; y++)
        for (var x = 0; x < rows[y].Length; x++)
            if (TemplateLegend.TraversableSymbols.Contains(rows[y][x]) || rows[y][x] == '#') cells.Add((x, y));

        Assert.NotEmpty(cells);
        var open = cells.ToHashSet();
        var seen = new HashSet<(int, int)> { cells[0] };
        var queue = new Queue<(int X, int Y)>([cells[0]]);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var n = (x + dx, y + dy);
                if (open.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            }
        }

        Assert.Equal(open.Count, seen.Count);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Template_HasAnEntrance(string id)
    {
        // Some walkable (or diggable) square must touch a pierceable outer wall ('%'), so tunnels
        // can get in — many of Angband's vaults are entered by tunnelling through their granite.
        var rows = TestData.Game.Templates.Single(t => t.Id == id).Rows;
        var entrance = false;
        for (var y = 0; y < rows.Count && !entrance; y++)
        for (var x = 0; x < rows[y].Length && !entrance; x++)
        {
            if (!TemplateLegend.TraversableSymbols.Contains(rows[y][x]) && rows[y][x] != '#') continue;
            foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
            {
                int nx = x + dx, ny = y + dy;
                if (ny >= 0 && ny < rows.Count && nx >= 0 && nx < rows[ny].Length && rows[ny][nx] == '%')
                    entrance = true;
            }
        }
        Assert.True(entrance);
    }

    [Fact]
    public void Rotation_FourTimes_IsIdentity()
    {
        var rows = TestData.Game.Templates.First(t => t.Kind == MapTemplateKind.MediumVault).Rows;
        IReadOnlyList<string> r = rows;
        for (var i = 0; i < 4; i++) r = TemplateLegend.RotateClockwise(r);
        Assert.Equal(rows, r);
    }

    [Fact]
    public void Rotation_SwapsDimensions()
    {
        var rotated = TemplateLegend.RotateClockwise(["ab", "cd", "ef"]);
        Assert.Equal(["eca", "fdb"], rotated);
    }
}
