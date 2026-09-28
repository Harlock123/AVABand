using Angband.Avalonia.Rendering;
using Angband.Avalonia.ViewModels;
using Angband.Data.Tiles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Angband.Avalonia.Controls;

/// <summary>
/// Draws the dungeon as a grid of cells with a camera that follows <see cref="IMapSource.Focus"/>.
/// The look comes from an <see cref="IMapRenderer"/>: ASCII glyphs, or a tileset when
/// <see cref="UseTiles"/> is on and a <see cref="Tileset"/> is chosen. Switching is instant.
/// </summary>
public sealed class MapView : Control
{
    public static readonly StyledProperty<IMapSource?> SourceProperty =
        AvaloniaProperty.Register<MapView, IMapSource?>(nameof(Source));

    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<MapView, int>(nameof(Revision));

    public static readonly StyledProperty<double> CellFontSizeProperty =
        AvaloniaProperty.Register<MapView, double>(nameof(CellFontSize), 16);

    public static readonly StyledProperty<bool> UseTilesProperty =
        AvaloniaProperty.Register<MapView, bool>(nameof(UseTiles));

    public static readonly StyledProperty<TilesetManifest?> TilesetProperty =
        AvaloniaProperty.Register<MapView, TilesetManifest?>(nameof(Tileset));

    public static readonly StyledProperty<double> TileScaleProperty =
        AvaloniaProperty.Register<MapView, double>(nameof(TileScale), 1.0);

    /// <summary>Scale tiles so the whole map fits (used by previews); ignores <see cref="TileScale"/>.</summary>
    public static readonly StyledProperty<bool> FitToBoundsProperty =
        AvaloniaProperty.Register<MapView, bool>(nameof(FitToBounds));

    /// <summary>
    /// Keep the focus centred all the time (Angband center_player); otherwise the view jumps to re-centre
    /// only when the focus comes within 3 squares of its edge, as Angband's panels do.
    /// </summary>
    public static readonly StyledProperty<bool> CenterPlayerProperty =
        AvaloniaProperty.Register<MapView, bool>(nameof(CenterPlayer), true);

    public bool CenterPlayer
    {
        get => GetValue(CenterPlayerProperty);
        set => SetValue(CenterPlayerProperty, value);
    }

    /// <summary>Projections and damage numbers to animate over the map.</summary>
    public static readonly StyledProperty<MapEffects?> EffectsProperty =
        AvaloniaProperty.Register<MapView, MapEffects?>(nameof(Effects));

    public MapEffects? Effects
    {
        get => GetValue(EffectsProperty);
        set => SetValue(EffectsProperty, value);
    }

    // The animation clock: runs only while there is something to animate.
    private global::Avalonia.Threading.DispatcherTimer? _clock;
    private readonly System.Diagnostics.Stopwatch _clockWatch = new();

    private void StartClock()
    {
        _clock ??= new global::Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(16),
            global::Avalonia.Threading.DispatcherPriority.Render, (_, _) => Tick());
        _clockWatch.Restart();
        _clock.Start();
        InvalidateVisual();
    }

    private void Tick()
    {
        var effects = Effects;
        var ms = _clockWatch.Elapsed.TotalMilliseconds;
        _clockWatch.Restart();
        effects?.Advance(ms);
        InvalidateVisual();
        if (effects is null || !effects.IsActive) _clock?.Stop();
    }

    /// <summary>Atlases are shared across views and kept while the app runs (tilesets are small).</summary>
    private static readonly Dictionary<string, TileAtlas> Atlases = new(StringComparer.Ordinal);

    private IMapRenderer? _renderer;
    // Camera of the last frame, for turning clicks into map squares.
    private (int OffsetX, int OffsetY, double OriginX, double OriginY, Size Cell) _camera;

    /// <summary>A map square was clicked; the flag is true for the secondary (right) button.</summary>
    public event Action<Angband.Core.Geometry.Loc, bool>? CellClicked;

    /// <summary>Mouse wheel: positive to zoom in, negative to zoom out.</summary>
    public event Action<int>? Zoom;

    public MapView()
    {
        PointerPressed += (_, e) =>
        {
            if (Source is null || _camera.Cell.Width <= 0) return;
            var p = e.GetPosition(this);
            var x = _camera.OffsetX + (int)Math.Floor((p.X - _camera.OriginX) / _camera.Cell.Width);
            var y = _camera.OffsetY + (int)Math.Floor((p.Y - _camera.OriginY) / _camera.Cell.Height);
            if (x < 0 || y < 0 || x >= Source.Width || y >= Source.Height) return;
            var secondary = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;
            CellClicked?.Invoke(new Angband.Core.Geometry.Loc(x, y), secondary);
            e.Handled = true;
        };
        PointerWheelChanged += (_, e) =>
        {
            Zoom?.Invoke(Math.Sign(e.Delta.Y));
            e.Handled = true;
        };
    }

    static MapView()
    {
        AffectsRender<MapView>(SourceProperty, RevisionProperty, CellFontSizeProperty, UseTilesProperty,
            TilesetProperty, TileScaleProperty, FitToBoundsProperty);
    }

    public IMapSource? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public int Revision
    {
        get => GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public double CellFontSize
    {
        get => GetValue(CellFontSizeProperty);
        set => SetValue(CellFontSizeProperty, value);
    }

    public bool UseTiles
    {
        get => GetValue(UseTilesProperty);
        set => SetValue(UseTilesProperty, value);
    }

    public TilesetManifest? Tileset
    {
        get => GetValue(TilesetProperty);
        set => SetValue(TilesetProperty, value);
    }

    public double TileScale
    {
        get => GetValue(TileScaleProperty);
        set => SetValue(TileScaleProperty, value);
    }

    public bool FitToBounds
    {
        get => GetValue(FitToBoundsProperty);
        set => SetValue(FitToBoundsProperty, value);
    }

    /// <summary>The renderer currently in use (exposed for tests and diagnostics).</summary>
    public IMapRenderer Renderer => _renderer ??= CreateRenderer();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EffectsProperty)
        {
            if (change.OldValue is MapEffects old) old.Started -= StartClock;
            if (change.NewValue is MapEffects now)
            {
                now.Started += StartClock;
                if (now.IsActive) StartClock();
            }
        }
        if (change.Property == CellFontSizeProperty || change.Property == UseTilesProperty
            || change.Property == TilesetProperty || change.Property == TileScaleProperty
            || change.Property == FitToBoundsProperty || (change.Property == BoundsProperty && FitToBounds))
            _renderer = null;
    }

    private IMapRenderer CreateRenderer()
    {
        if (UseTiles && Tileset is { } manifest)
        {
            if (!Atlases.TryGetValue(manifest.Directory, out var atlas))
                Atlases[manifest.Directory] = atlas = new TileAtlas(manifest);
            return new TileRenderer(atlas, FitToBounds ? FittedScale(manifest) : Math.Clamp(TileScale, 0.25, 8));
        }
        return new AsciiRenderer(CellFontSize);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var source = Source;
        if (source is null) return;

        var renderer = Renderer;
        var cell = renderer.CellSize;
        var cols = Math.Max(1, (int)(Bounds.Width / cell.Width));
        var rows = Math.Max(1, (int)(Bounds.Height / cell.Height));

        // Camera: centre on the focus, clamp to the map; centre the whole map if it fits.
        var (offsetX, originX) = Axis(source.Width, cols, source.Focus.X, Bounds.Width, cell.Width, CenterPlayer ? null : _camera.OffsetX);
        var (offsetY, originY) = Axis(source.Height, rows, source.Focus.Y, Bounds.Height, cell.Height, CenterPlayer ? null : _camera.OffsetY);
        _camera = (offsetX, offsetY, originX, originY, cell);
        var endX = Math.Min(source.Width, offsetX + cols);
        var endY = Math.Min(source.Height, offsetY + rows);

        // Pixel art stays crisp when scaled up.
        using var _ = context.PushRenderOptions(new RenderOptions
        {
            BitmapInterpolationMode = renderer is TileRenderer { Scale: < 1 }
                ? BitmapInterpolationMode.HighQuality
                : BitmapInterpolationMode.None,
        });

        for (var y = offsetY; y < endY; y++)
        for (var x = offsetX; x < endX; x++)
        {
            var dest = new Rect(originX + (x - offsetX) * cell.Width, originY + (y - offsetY) * cell.Height, cell.Width, cell.Height);
            renderer.DrawCell(context, dest, source.GetCell(x, y));
        }

        // The target (red corners) and the look/target cursor (a yellow box), over the map.
        Rect? CellRect(Angband.Core.Geometry.Loc p) =>
            p.X >= offsetX && p.X < endX && p.Y >= offsetY && p.Y < endY
                ? new Rect(originX + (p.X - offsetX) * cell.Width, originY + (p.Y - offsetY) * cell.Height, cell.Width, cell.Height)
                : null;
        if (source.Target is { } target && CellRect(target) is { } t) DrawCorners(context, t, TargetPen);
        if (source.Cursor is { } cursor && CellRect(cursor) is { } c) context.DrawRectangle(null, CursorPen, c.Deflate(1));
        if (source.Highlight is { } highlight && CellRect(highlight) is { } h) context.DrawRectangle(null, HighlightPen, h.Deflate(0.5));

        if (Effects is { IsActive: true } effects) DrawEffects(context, effects, CellRect, cell);
    }

    private AsciiRenderer? _effectText;

    /// <summary>
    /// Bolts, beams and bursts as Angband draws them (coloured glyphs), over tiles too, with a
    /// shadow so they read on any background; then damage numbers rising and fading.
    /// </summary>
    private void DrawEffects(DrawingContext context, MapEffects effects, Func<Angband.Core.Geometry.Loc, Rect?> cellRect, Size cell)
    {
        var size = Math.Max(8, Math.Floor(cell.Height * 0.8));
        if (_effectText is null || Math.Abs(_effectText.FontSize - size) > 0.1) _effectText = new AsciiRenderer(size);
        foreach (var g in effects.Glyphs)
        {
            if (cellRect(g.Loc) is not { } r) continue;
            _effectText.DrawGlyph(context, r.Translate(new Vector(1, 1)), g.Glyph, 0xFF000000);
            _effectText.DrawGlyph(context, r, g.Glyph, g.Argb);
        }
        foreach (var n in effects.Numbers)
        {
            if (cellRect(n.Loc) is not { } r) continue;
            var alpha = (byte)(255 * Math.Clamp(1 - n.Age * n.Age, 0, 1));
            var rise = n.Age * cell.Height * 0.9;
            var text = new FormattedText(n.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                NumberFace, Math.Max(9, cell.Height * 0.6), new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xE0, 0x40)));
            var at = new Point(r.X + (r.Width - text.Width) / 2, r.Y - rise - text.Height / 3);
            var shadow = new FormattedText(n.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                NumberFace, Math.Max(9, cell.Height * 0.6), new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)));
            context.DrawText(shadow, at + new Vector(1, 1));
            context.DrawText(text, at);
        }
    }

    private static readonly Typeface NumberFace = new(new FontFamily(AsciiRenderer.MonoFontUri), FontStyle.Normal, FontWeight.Bold);

    private static readonly IPen HighlightPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x40)), 1);

    private static readonly IPen TargetPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x40, 0x40)), 2);
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x40)), 2);

    private static void DrawCorners(DrawingContext context, Rect r, IPen pen)
    {
        var len = Math.Max(3, r.Width / 3);
        foreach (var (corner, dx, dy) in new[] { (r.TopLeft, 1, 1), (r.TopRight, -1, 1), (r.BottomLeft, 1, -1), (r.BottomRight, -1, -1) })
        {
            context.DrawLine(pen, corner, corner + new Vector(dx * len, 0));
            context.DrawLine(pen, corner, corner + new Vector(0, dy * len));
        }
    }

    /// <summary>The largest half-step scale at which the whole map fits the control.</summary>
    private double FittedScale(TilesetManifest manifest)
    {
        if (Source is not { } source || Bounds.Width <= 0 || Bounds.Height <= 0) return 1;
        var fit = Math.Min(Bounds.Width / (source.Width * manifest.TileWidth), Bounds.Height / (source.Height * manifest.TileHeight));
        return fit >= 0.5 ? Math.Min(8, Math.Floor(fit * 2) / 2) : Math.Max(0.25, Math.Floor(fit * 4) / 4); // quarter steps below half size (the level map)
    }

    /// <param name="panel">The last offset, for panel scrolling (null: always centre on the focus).</param>
    private static (int Offset, double Origin) Axis(int mapSize, int viewCells, int focus, double pixels, double cellSize, int? panel = null)
    {
        if (mapSize <= viewCells) return (0, Math.Floor((pixels - mapSize * cellSize) / 2));
        var max = mapSize - viewCells;
        // Angband verify_panel: scroll when within 3 squares of the edge (the edge of the map excepted).
        if (panel is { } last && Math.Clamp(last, 0, max) is var kept
            && (focus >= kept + 3 || kept == 0) && (focus < kept + viewCells - 3 || kept == max))
            return (kept, 0);
        return (Math.Clamp(focus - viewCells / 2, 0, max), 0);
    }
}
