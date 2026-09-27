using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>
/// Symbols used in room and vault templates (following Angband's vault.txt conventions):
/// <list type="table">
/// <item><term>space</term><description>untouched (outside the room)</description></item>
/// <item><term>%</term><description>outer granite wall (tunnels may pierce)</description></item>
/// <item><term>#</term><description>inner granite wall</description></item>
/// <item><term>X</term><description>permanent wall</description></item>
/// <item><term>.</term><description>floor</description></item>
/// <item><term>+</term><description>secret door</description></item>
/// <item><term>D</term><description>closed (possibly locked) door</description></item>
/// <item><term>^</term><description>trap</description></item>
/// <item><term>*</term><description>treasure or trap</description></item>
/// <item><term>,</term><description>monster (+3) or object (+7)</description></item>
/// <item><term>&amp;</term><description>monster (+5)</description></item>
/// <item><term>@</term><description>monster (+11)</description></item>
/// <item><term>9</term><description>monster (+9) and good object (+7)</description></item>
/// <item><term>8</term><description>monster (+40) and great object (+20)</description></item>
/// <item><term>$</term><description>gold</description></item>
/// <item><term>0</term><description>monster (+20)</description></item>
/// <item><term>3 5 7</term><description>object (+3, +7, +15)</description></item>
/// <item><term>4</term><description>monster (+3) and/or object (+7), even odds each</description></item>
/// <item><term>letters</term><description>a monster shown with that glyph (not <c>D</c> or <c>X</c>)</description></item>
/// <item><term>&lt; &gt;</term><description>up / down staircase</description></item>
/// <item><term>:</term><description>passable rubble</description></item>
/// <item><term>;</term><description>rubble</description></item>
/// <item><term>~</term><description>lava</description></item>
/// </list>
/// </summary>
public static class TemplateLegend
{
    public const string KnownSymbols = " %#X.+D^*,&@98$<>:;~03457";

    /// <summary>Symbols a character can walk through (possibly after opening a door or clearing rubble).</summary>
    public const string TraversableSymbols = ".+D^*,&@98$<>:;03457abcdefghijklmnopqrstuvwxyzABCEFGHIJKLMNOPQRSTUVWYZ";

    public static bool IsKnown(char c) => KnownSymbols.Contains(c) || IsMonsterGlyph(c);

    /// <summary>A letter placing a monster of that glyph (Angband vaults: 'o' for orcs, 'T' for trolls...).</summary>
    public static bool IsMonsterGlyph(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' && c is not ('D' or 'X');

    internal static void Apply(GenContext c, Loc p, char symbol, SquareFlags flags)
    {
        switch (symbol)
        {
            case ' ':
                return;
            case '%':
                c.SetWall(p, c.F.Granite, SquareFlags.WallOuter, flags);
                return;
            case '#':
                c.SetWall(p, c.F.Granite, SquareFlags.WallInner, flags);
                return;
            case 'X':
                c.SetWall(p, c.F.Permanent, SquareFlags.WallInner, flags);
                return;
        }

        c.SetFloor(p, flags);
        switch (symbol)
        {
            case '+': c.PlaceSecretDoor(p); break;
            case 'D': c.PlaceClosedDoor(p); break;
            case '^': c.PlaceTrap(p); break;
            case '*':
                if (c.Rng.OneIn(2)) c.PlaceTrap(p);
                else c.AddHint(p, SpawnKind.Object, 3);
                break;
            case ',': c.AddHint(p, SpawnKind.MonsterOrObject, 3); break;
            case '&': c.AddHint(p, SpawnKind.Monster, 5); break;
            case '@': c.AddHint(p, SpawnKind.Monster, 11); break;
            case '9':
                c.AddHint(p, SpawnKind.Monster, 9);
                c.AddHint(p, SpawnKind.GoodObject, 7);
                break;
            case '8':
                c.AddHint(p, SpawnKind.Monster, 40);
                c.AddHint(p, SpawnKind.GreatObject, 20);
                break;
            case '$': c.AddHint(p, SpawnKind.Gold); break;
            case '0': c.AddHint(p, SpawnKind.Monster, 20); break;
            case '3': c.AddHint(p, SpawnKind.Object, 3); break;
            case '5': c.AddHint(p, SpawnKind.Object, 7); break;
            case '7': c.AddHint(p, SpawnKind.Object, 15); break;
            case '4':
                if (c.Rng.OneIn(2)) c.AddHint(p, SpawnKind.Monster, 3);
                if (c.Rng.OneIn(2)) c.AddHint(p, SpawnKind.Object, 7);
                break;
            case var g when IsMonsterGlyph(g): c.AddHint(p, SpawnKind.Monster, 5, "glyph:" + g); break;
            case '<': c.SetFeature(p, c.F.UpStair); break;
            case '>': c.SetFeature(p, c.F.DownStair); break;
            case ':': c.SetFeature(p, c.F.PassableRubble); break;
            case ';': c.SetFeature(p, c.F.Rubble); break;
            case '~': c.SetFeature(p, c.F.Lava); break;
        }
    }

    /// <summary>Rotates a layout 90 degrees clockwise.</summary>
    public static string[] RotateClockwise(IReadOnlyList<string> rows)
    {
        var h = rows.Count;
        var w = rows[0].Length;
        var result = new string[w];
        for (var r = 0; r < w; r++)
        {
            var chars = new char[h];
            for (var col = 0; col < h; col++) chars[col] = rows[h - 1 - col][r];
            result[r] = new string(chars);
        }
        return result;
    }

    public static string[] MirrorHorizontally(IReadOnlyList<string> rows) =>
        rows.Select(r => new string(r.Reverse().ToArray())).ToArray();
}
