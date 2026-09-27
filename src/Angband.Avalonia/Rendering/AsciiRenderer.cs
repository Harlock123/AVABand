using System.Globalization;
using Angband.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Angband.Avalonia.Rendering;

/// <summary>The classic look: one coloured glyph per cell. Glyphs are formatted once and cached.</summary>
public sealed class AsciiRenderer : IMapRenderer
{
    private static readonly Typeface Mono = new(new FontFamily(
        "Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace"));

    private readonly Dictionary<(char, uint), FormattedText> _glyphs = [];
    private readonly Dictionary<uint, IBrush> _brushes = [];

    public AsciiRenderer(double fontSize)
    {
        FontSize = fontSize;
        var probe = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, fontSize, Brushes.White);
        CellSize = new Size(Math.Ceiling(probe.WidthIncludingTrailingWhitespace), Math.Ceiling(probe.Height));
    }

    public double FontSize { get; }
    public Size CellSize { get; }

    public void DrawCell(DrawingContext context, Rect dest, in MapCell cell)
    {
        if ((cell.Background & 0x00FFFFFF) != 0) context.FillRectangle(Brush(cell.Background), dest);
        DrawGlyph(context, dest, cell.Glyph, cell.Foreground);
    }

    /// <summary>Draws a glyph centred in <paramref name="dest"/> (also used as the tile-mode fallback).</summary>
    public void DrawGlyph(DrawingContext context, Rect dest, char glyph, uint argb)
    {
        if (glyph == ' ') return;
        var text = Glyph(glyph, argb);
        context.DrawText(text, new Point(dest.X + (dest.Width - text.Width) / 2, dest.Y + (dest.Height - text.Height) / 2));
    }

    public IBrush Brush(uint argb)
    {
        if (!_brushes.TryGetValue(argb, out var brush))
            _brushes[argb] = brush = new ImmutableSolidColorBrush(Color.FromUInt32(argb));
        return brush;
    }

    private FormattedText Glyph(char glyph, uint argb)
    {
        if (!_glyphs.TryGetValue((glyph, argb), out var text))
        {
            text = new FormattedText(glyph.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Mono, FontSize, Brush(argb));
            _glyphs[(glyph, argb)] = text;
        }
        return text;
    }
}
