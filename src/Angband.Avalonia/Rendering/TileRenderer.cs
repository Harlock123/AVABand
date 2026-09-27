using Angband.Avalonia.ViewModels;
using Angband.Data.Tiles;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Angband.Avalonia.Rendering;

/// <summary>
/// Draws cells from a tileset. Creatures and traps are drawn over their terrain when the tileset is
/// transparent; tiles without art for a lighting state are shaded (dimmed when remembered, warmed
/// when torch-lit); keys the tileset lacks fall back to ASCII glyphs.
/// </summary>
public sealed class TileRenderer : IMapRenderer
{
    private static readonly IBrush RememberedShade = new ImmutableSolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    private static readonly IBrush TorchTint = new ImmutableSolidColorBrush(Color.FromArgb(40, 255, 200, 100));

    private readonly TileAtlas _atlas;
    private readonly AsciiRenderer _fallback;

    public TileRenderer(TileAtlas atlas, double scale)
    {
        _atlas = atlas;
        Scale = scale;
        CellSize = new Size(Math.Max(4, Math.Round(atlas.Manifest.TileWidth * scale)),
            Math.Max(4, Math.Round(atlas.Manifest.TileHeight * scale)));
        _fallback = new AsciiRenderer(CellSize.Height * 0.75);
    }

    public double Scale { get; }
    public Size CellSize { get; }

    public void DrawCell(DrawingContext context, Rect dest, in MapCell cell)
    {
        if (cell.IsUnknown) return;
        var opaque = _atlas.Manifest.Opaque;
        var hasUnder = cell.UnderKey is not null;

        if (hasUnder && !opaque) DrawTile(context, dest, cell.UnderKey!, cell.UnderLighting);

        if (DrawTile(context, dest, cell.TileKey, cell.Lighting)) return;
        if (cell.AltTileKey is { } alt && DrawTile(context, dest, alt, cell.Lighting)) return;
        // Any creature the tileset has no picture for is drawn as a typical one of its kind.
        if (cell.TileKey.StartsWith("monster:", StringComparison.Ordinal)
            && DrawTile(context, dest, GlyphKey(cell.Glyph), cell.Lighting)) return;

        // No art for this key: keep the terrain (if any) and draw the glyph on top.
        if (hasUnder && opaque) DrawTile(context, dest, cell.UnderKey!, cell.UnderLighting);
        _fallback.DrawGlyph(context, dest, cell.Glyph, cell.Foreground);
    }

    /// <summary>The per-glyph fallback key for monsters (<c>monster-glyph:o</c> for any orc).</summary>
    public static string GlyphKey(char glyph) => "monster-glyph:" + glyph;

    private bool DrawTile(DrawingContext context, Rect dest, string key, TileLighting lighting)
    {
        if (!_atlas.TryResolve(key, lighting, out var layers, out var exact)) return false;
        foreach (var layer in layers) context.DrawImage(layer.Image, layer.Source, dest);
        if (!exact)
        {
            if (lighting == TileLighting.Dark) context.FillRectangle(RememberedShade, dest);
            else if (lighting == TileLighting.Torch) context.FillRectangle(TorchTint, dest);
        }
        return true;
    }
}
