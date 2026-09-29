using Angband.Core.Generation;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>4.2.5's vaults and room templates, as vault.txt and room_template.txt have them.</summary>
public class TemplateTests
{
    [Fact]
    public void All_of_4_2_5s_vaults_and_room_templates()
    {
        var data = TestData.Game;
        Assert.Equal(162, data.Vaults.Count);
        Assert.Equal(500, data.RoomTemplates.Count);
        Assert.Equal(66, data.Vaults.Count(v => v.Type == "Interesting room"));
        Assert.All(data.Vaults, v => Assert.All(v.Rows, r => Assert.Equal(v.Width, r.Length)));
        Assert.Contains(data.Vaults, v => v.Has("FEW_ENTRANCES"));
        Assert.Contains(data.RoomTemplates, r => r.Tval == "potion");
    }

    [Fact]
    public void Four_quarter_turns_are_no_turn()
    {
        var g = new Loc(3, 1);
        int h = 5, w = 8;
        var p = g;
        for (var i = 0; i < 4; i++)
        {
            p = Cave.SymmetryTransform(p, 0, 0, h, w, 1, false);
            (h, w) = (w, h);
        }
        Assert.Equal(g, p);
    }

    [Fact]
    public void A_quarter_turn_swaps_the_sides()
    {
        // A 2-row, 3-column block turned clockwise: its top-left corner goes to the top-right.
        Assert.Equal(new Loc(1, 0), Cave.SymmetryTransform(new Loc(0, 0), 0, 0, 2, 3, 1, false));
        Assert.Equal(new Loc(0, 2), Cave.SymmetryTransform(new Loc(2, 1), 0, 0, 2, 3, 1, false));
        Assert.Equal(new Loc(2, 0), Cave.SymmetryTransform(new Loc(0, 0), 0, 0, 2, 3, 0, true));
    }
}
