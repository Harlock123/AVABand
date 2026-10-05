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

    /// <summary>Each change centres the view on the focus once (Angband's Ctrl+L, center_panel).</summary>
    public static readonly StyledProperty<int> RecentreCountProperty =
        AvaloniaProperty.Register<MapView, int>(nameof(RecentreCount));

    public int RecentreCount
    {
        get => GetValue(RecentreCountProperty);
        set => SetValue(RecentreCountProperty, value);
    }

    private bool _recentre;

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

    /// <summary>The top-left square of the last frame (for tests).</summary>
    public Angband.Core.Geometry.Loc ViewOffset => new(_camera.OffsetX, _camera.OffsetY);

    /// <summary>How many squares the last frame showed across and down (for tests).</summary>
    public (int Cols, int Rows) ViewCells { get; private set; }

    /// <summary>A map square was clicked; the flag is true for the secondary (right) button.</summary>
    public event Action<Angband.Core.Geometry.Loc, bool>? CellClicked;

    /// <summary>A middle-button drag moved the map by so many squares (the view's centre goes the other way).</summary>
    public event Action<int, int>? Pan;

    /// <summary>The middle button was let go (or the drag lost): the view comes back.</summary>
    public event Action? PanEnded;

    /// <summary>Squares a notch of the wheel slides the view, with Shift held.</summary>
    public const double WheelPanSquares = 3;

    /// <summary>Shift-wheel movement not yet a whole square (a trackpad's small steps add up).</summary>
    private (double X, double Y) _wheelPan;

    /// <summary>Where a middle-button drag last moved the view from, while one is under way.</summary>
    private Point? _panFrom;

    /// <summary>Mouse wheel: positive to zoom in, negative to zoom out.</summary>
    public event Action<int>? Zoom;

    /// <summary>The square under the pointer changed (null when it left the map).</summary>
    public event Action<Angband.Core.Geometry.Loc?>? CellHovered;

    private Angband.Core.Geometry.Loc? _hovered;

    /// <summary>The map square at a point in the view, if it is on the map.</summary>
    private Angband.Core.Geometry.Loc? CellAt(Point p)
    {
        if (Source is null || _camera.Cell.Width <= 0) return null;
        var x = _camera.OffsetX + (int)Math.Floor((p.X - _camera.OriginX) / _camera.Cell.Width);
        var y = _camera.OffsetY + (int)Math.Floor((p.Y - _camera.OriginY) / _camera.Cell.Height);
        return x < 0 || y < 0 || x >= Source.Width || y >= Source.Height ? null : new Angband.Core.Geometry.Loc(x, y);
    }

    private void Hover(Angband.Core.Geometry.Loc? cell)
    {
        if (cell == _hovered) return;
        _hovered = cell;
        CellHovered?.Invoke(cell);
    }

    public MapView()
    {
        PointerPressed += (_, e) =>
        {
            if (Source is null || _camera.Cell.Width <= 0) return;
            var p = e.GetPosition(this);
            if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            {
                // A middle-button drag looks around the map (AVABand's own).
                _panFrom = p;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
            var x = _camera.OffsetX + (int)Math.Floor((p.X - _camera.OriginX) / _camera.Cell.Width);
            var y = _camera.OffsetY + (int)Math.Floor((p.Y - _camera.OriginY) / _camera.Cell.Height);
            if (x < 0 || y < 0 || x >= Source.Width || y >= Source.Height) return;
            var secondary = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;
            CellClicked?.Invoke(new Angband.Core.Geometry.Loc(x, y), secondary);
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (_panFrom is { } from && _camera.Cell.Width > 0)
            {
                // The map follows the mouse: dragging right shows what lies to the west.
                var p = e.GetPosition(this);
                var dx = (int)((from.X - p.X) / _camera.Cell.Width);
                var dy = (int)((from.Y - p.Y) / _camera.Cell.Height);
                if (dx == 0 && dy == 0) return;
                _panFrom = new Point(from.X - dx * _camera.Cell.Width, from.Y - dy * _camera.Cell.Height);
                Pan?.Invoke(dx, dy);
                return;
            }
            Hover(CellAt(e.GetPosition(this)));
        };
        PointerReleased += (_, e) =>
        {
            if (_panFrom is null || e.InitialPressMouseButton != global::Avalonia.Input.MouseButton.Middle) return;
            _panFrom = null;
            e.Pointer.Capture(null);
            PanEnded?.Invoke();
        };
        PointerCaptureLost += (_, _) =>
        {
            if (_panFrom is null) return;
            _panFrom = null;
            PanEnded?.Invoke();
        };
        PointerExited += (_, _) => Hover(null);
        PointerWheelChanged += (_, e) =>
        {
            e.Handled = true;
            if ((e.KeyModifiers & global::Avalonia.Input.KeyModifiers.Shift) != 0)
            {
                // Shift with the wheel (or two fingers on a trackpad) looks around (AVABand's own): a notch
                // slides the view three squares, a trackpad's small steps add up; letting go of Shift ends it.
                // (Shift turns a wheel sideways on some systems: either way, either axis.)
                _wheelPan = (_wheelPan.X - e.Delta.X * WheelPanSquares, _wheelPan.Y - e.Delta.Y * WheelPanSquares);
                var (dx, dy) = ((int)_wheelPan.X, (int)_wheelPan.Y);
                if (dx == 0 && dy == 0) return;
                _wheelPan = (_wheelPan.X - dx, _wheelPan.Y - dy);
                Pan?.Invoke(dx, dy);
                return;
            }
            _wheelPan = (0, 0);
            Zoom?.Invoke(Math.Sign(e.Delta.Y));
        };
    }

    static MapView()
    {
        AffectsRender<MapView>(SourceProperty, RevisionProperty, CellFontSizeProperty, UseTilesProperty,
            TilesetProperty, TileScaleProperty, FitToBoundsProperty, RecentreCountProperty);
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

    /// <summary>How many whole squares the view shows across and down.</summary>
    public (int Cols, int Rows) VisibleCells
    {
        get
        {
            var cell = Renderer.CellSize;
            return cell.Width <= 0 || Bounds.Width <= 0 ? (66, 22)
                : (Math.Max(1, (int)(Bounds.Width / cell.Width)), Math.Max(1, (int)(Bounds.Height / cell.Height)));
        }
    }

    /// <summary>The renderer currently in use (exposed for tests and diagnostics).</summary>
    public IMapRenderer Renderer => _renderer ??= CreateRenderer();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RecentreCountProperty) _recentre = true;
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
        var follow = CenterPlayer || _recentre;
        _recentre = false;
        var (offsetX, originX) = Axis(source.Width, cols, source.Focus.X, Bounds.Width, cell.Width, follow ? null : _camera.OffsetX);
        var (offsetY, originY) = Axis(source.Height, rows, source.Focus.Y, Bounds.Height, cell.Height, follow ? null : _camera.OffsetY);
        _camera = (offsetX, offsetY, originX, originY, cell);
        ViewCells = (cols, rows);
        var endX = Math.Min(source.Width, offsetX + cols);
        var endY = Math.Min(source.Height, offsetY + rows);

        // Pixel art stays crisp when scaled up.
        using var _ = context.PushRenderOptions(new RenderOptions
        {
            BitmapInterpolationMode = renderer is TileRenderer { Scale: < 1 }
                ? BitmapInterpolationMode.HighQuality
                : BitmapInterpolationMode.None,
        });

        var shading = source.LightAndShadow;
        var anyTorch = false;
        var flicker = Flicker();
        var night = source.IsNight;
        List<(Rect Dest, ShopFacade Part)>? lit = null; // (windows and doors, lit again over the shading at night)
        for (var y = offsetY; y < endY; y++)
        for (var x = offsetX; x < endX; x++)
        {
            var dest = new Rect(originX + (x - offsetX) * cell.Width, originY + (y - offsetY) * cell.Height, cell.Width, cell.Height);
            var mapCell = source.GetCell(x, y);
            renderer.DrawCell(context, dest, mapCell);
            if (mapCell.Sconce) DrawSconce(context, dest, shading ? flicker : 1);
            if (!mapCell.IsUnknown && source.FacadeAt(x, y) is { } facade)
            {
                DrawFacade(context, dest, facade, renderer, night);
                if (night && facade.Kind != FacadeKind.Wall) (lit ??= []).Add((dest, facade));
            }
            anyTorch |= shading && mapCell.Sconce; // (its flame flickers with the torchlight)
            if (!shading || mapCell.IsUnknown) continue;
            var shade = source.ShadeAt(x, y);
            anyTorch |= shade.Torch;
            if (shade.Torch && shade.Warmth > 0)
                context.FillRectangle(WarmBrushes[(byte)Math.Clamp(255 * shade.Warmth * flicker, 0, 255)], dest);
            var amount = Math.Clamp(shade.Torch ? shade.Amount * (2 - flicker) : shade.Amount, 0, 1);
            if (amount > 0.01) context.FillRectangle(ShadeBrushes[(byte)(255 * amount)], dest);
        }
        // Map pins (AVABand's own): a little red flag on each, over the shading.
        for (var y = offsetY; y < endY; y++)
        for (var x = offsetX; x < endX; x++)
            if (source.HasPin(x, y))
                DrawPin(context, new Rect(originX + (x - offsetX) * cell.Width, originY + (y - offsetY) * cell.Height, cell.Width, cell.Height));

        // At night the shops' windows glow and their lanterns burn, over the dark.
        if (lit is not null)
            foreach (var (dest, part) in lit) DrawShopLight(context, dest, part, flicker);
        UpdateFlickerClock(shading && (anyTorch || lit is not null));
        DrawShopSigns(context, source, offsetX, offsetY, endX, endY, originX, originY, cell);

        // The target (red corners) and the look/target cursor (a yellow box), over the map.
        Rect? CellRect(Angband.Core.Geometry.Loc p) =>
            p.X >= offsetX && p.X < endX && p.Y >= offsetY && p.Y < endY
                ? new Rect(originX + (p.X - offsetX) * cell.Width, originY + (p.Y - offsetY) * cell.Height, cell.Width, cell.Height)
                : null;
        if (source.ShownPath.Count > 0) DrawPath(context, source.ShownPath, source.PathIsAim, CellRect, cell);
        if (source.Target is { } target && CellRect(target) is { } t) DrawCorners(context, t, TargetPen);
        if (source.Cursor is { } cursor && CellRect(cursor) is { } c) context.DrawRectangle(null, CursorPen, c.Deflate(1));
        if (source.Highlight is { } highlight && CellRect(highlight) is { } h) context.DrawRectangle(null, HighlightPen, h.Deflate(0.5));

        if (Effects is { IsActive: true } effects) DrawEffects(context, effects, CellRect, cell);
    }

    private static readonly IBrush RouteBrush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0x48, 0x40, 0xB0, 0xFF));
    private static readonly IBrush AimBrush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xC8, 0x30));
    private static readonly IPen AimEndPen = new global::Avalonia.Media.Immutable.ImmutablePen(
        new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xC8, 0x30)), 2);

    /// <summary>
    /// A route (squares tinted blue) or a line of fire (tinted yellow, with a ring on the square
    /// where the shot would stop), light enough that what is there still shows through.
    /// </summary>
    private static void DrawPath(DrawingContext context, IReadOnlyList<Angband.Core.Geometry.Loc> path, bool aim,
        Func<Angband.Core.Geometry.Loc, Rect?> cellRect, Size cell)
    {
        for (var i = 0; i < path.Count; i++)
        {
            if (cellRect(path[i]) is not { } r) continue;
            context.DrawRectangle(aim ? AimBrush : RouteBrush, null, r.Deflate(1));
            if (aim && i == path.Count - 1) context.DrawEllipse(null, AimEndPen, r.Center, r.Width * 0.45, r.Height * 0.45);
        }
    }

    private static readonly IBrush[] ShadeBrushes = [.. Enumerable.Range(0, 256).Select(a =>
        (IBrush)new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb((byte)a, 0, 0, 0)))];
    private static readonly IBrush[] WarmBrushes = [.. Enumerable.Range(0, 256).Select(a =>
        (IBrush)new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb((byte)a, 255, 150, 50)))];

    // AVABand's sconces on the walls of lit rooms: a brass bracket, a flame and its warm glow.
    private static readonly IBrush SconceGlow = new RadialGradientBrush
    {
        GradientStops = { new GradientStop(Color.FromArgb(0x78, 0xFF, 0xB8, 0x48), 0), new GradientStop(Color.FromArgb(0x00, 0xFF, 0xB8, 0x48), 1) },
    }.ToImmutable();
    private static readonly IBrush SconceBrass = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xA8, 0x7A, 0x30));
    private static readonly IBrush SconceFlame = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x20));
    private static readonly IBrush SconceCore = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xE8, 0x90));

    private static readonly IBrush PinFlag = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xE0, 0x30, 0x30));
    private static readonly IPen PinPole = new global::Avalonia.Media.Immutable.ImmutablePen(
        new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xF0, 0xE8, 0xD8)), 1.2);

    /// <summary>A map pin: a pole and a red pennant in the top corner of its square.</summary>
    private static void DrawPin(DrawingContext context, Rect cell)
    {
        var (w, h) = (Math.Max(cell.Width, 6), Math.Max(cell.Height, 6));
        var x = cell.X + w * 0.18;
        context.DrawLine(PinPole, new Point(x, cell.Y + h * 0.08), new Point(x, cell.Y + h * 0.62));
        var flag = new StreamGeometry();
        using (var g = flag.Open())
        {
            g.BeginFigure(new Point(x, cell.Y + h * 0.08), isFilled: true);
            g.LineTo(new Point(x + w * 0.42, cell.Y + h * 0.2));
            g.LineTo(new Point(x, cell.Y + h * 0.33));
            g.EndFigure(isClosed: true);
        }
        context.DrawGeometry(PinFlag, null, flag);
    }

    // --- Shopfronts in town (AVABand's own) -------------------------------------------------------

    private static readonly IBrush FrameBrush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0x3A, 0x26, 0x14));
    private static readonly IBrush DayGlass = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0xE0, 0xA8, 0xBC, 0xC8));
    private static readonly IBrush NightGlass = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0xF0, 0xE8, 0xA8, 0x50));
    private static readonly IBrush AsciiDayGlass = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0x70, 0x6A, 0x84, 0x96));
    private static readonly IBrush AsciiNightGlass = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0x90, 0xB0, 0x78, 0x30));
    private static readonly IBrush Curtain = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0x8A, 0x2A, 0x2A));
    private static readonly IBrush Canvas = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xEF, 0xE4, 0xC8));
    private static readonly IPen MullionPen = new global::Avalonia.Media.Immutable.ImmutablePen(
        new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0xC0, 0x3A, 0x26, 0x14)), 1);
    private static readonly IBrush WindowGlow = new RadialGradientBrush
    {
        GradientStops = { new GradientStop(Color.FromArgb(0x60, 0xFF, 0xC0, 0x60), 0), new GradientStop(Color.FromArgb(0x00, 0xFF, 0xC0, 0x60), 1) },
    }.ToImmutable();
    private static readonly Dictionary<uint, IBrush> ShopBrushes = [];

    private static IBrush ShopBrush(uint argb)
    {
        lock (ShopBrushes)
        {
            if (!ShopBrushes.TryGetValue(argb, out var brush))
                ShopBrushes[argb] = brush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromUInt32(argb));
            return brush;
        }
    }

    /// <summary>
    /// A shopfront square: its building's walls tinted the shop's colour; a window (a wooden frame, glass
    /// lit amber at night, the shop's ware behind it — or the Home's curtains); and over the street side
    /// of the windows and the door, a striped awning in the shop's colour.
    /// </summary>
    private static void DrawFacade(DrawingContext context, Rect cell, ShopFacade part, IMapRenderer renderer, bool night)
    {
        if (part.Kind == FacadeKind.Wall)
        {
            context.FillRectangle(ShopBrush((part.Colour & 0x00FFFFFF) | 0x3C000000), cell);
            return;
        }
        if (renderer is AsciiRenderer)
        {
            // In letters a window is its glass and the ware's symbol, and the awning is left out (too fine at this size).
            if (part.Kind != FacadeKind.Window) return;
            context.FillRectangle(night ? AsciiNightGlass : AsciiDayGlass, cell);
            if (part.Ware is { } letter) renderer.DrawCell(context, cell, letter);
            else context.FillRectangle(Curtain, cell.Deflate(cell.Width * 0.2));
            return;
        }
        if (part.Kind == FacadeKind.Window)
        {
            var w = cell.Width;
            context.FillRectangle(FrameBrush, cell.Deflate(w * 0.08));
            var glass = cell.Deflate(w * 0.16);
            context.FillRectangle(night ? NightGlass : DayGlass, glass);
            if (part.Ware is { } ware) renderer.DrawCell(context, glass.Deflate(w * 0.05), ware);
            else
            {
                // The Home: curtains drawn.
                context.FillRectangle(Curtain, new Rect(glass.X, glass.Y, glass.Width * 0.42, glass.Height));
                context.FillRectangle(Curtain, new Rect(glass.Right - glass.Width * 0.42, glass.Y, glass.Width * 0.42, glass.Height));
            }
            context.DrawLine(MullionPen, new Point(glass.Center.X, glass.Top), new Point(glass.Center.X, glass.Bottom));
        }
        DrawAwning(context, cell, part);
    }

    /// <summary>A striped awning along the street side of a square, in the shop's colour and canvas white.</summary>
    private static void DrawAwning(DrawingContext context, Rect cell, ShopFacade part)
    {
        const double depth = 0.24;
        const int stripes = 4;
        var colour = ShopBrush(part.Colour);
        if (part.StreetY != 0)
        {
            var h = cell.Height * depth;
            var y = part.StreetY > 0 ? cell.Bottom - h : cell.Top;
            var sw = cell.Width / stripes;
            for (var i = 0; i < stripes; i++)
                context.FillRectangle(i % 2 == 0 ? colour : Canvas, new Rect(cell.X + i * sw, y, sw, h));
        }
        else
        {
            var wd = cell.Width * depth;
            var x = part.StreetX > 0 ? cell.Right - wd : cell.Left;
            var sh = cell.Height / stripes;
            for (var i = 0; i < stripes; i++)
                context.FillRectangle(i % 2 == 0 ? colour : Canvas, new Rect(x, cell.Y + i * sh, wd, sh));
        }
    }

    /// <summary>At night: a window's warm light spilling out, and a lantern burning beside each door.</summary>
    private static void DrawShopLight(DrawingContext context, Rect cell, ShopFacade part, double flicker)
    {
        var (w, h) = (cell.Width, cell.Height);
        if (part.Kind == FacadeKind.Window)
        {
            var glow = new Rect(cell.X - w * 0.6 + part.StreetX * w * 0.5, cell.Y - h * 0.6 + part.StreetY * h * 0.5, w * 2.2, h * 2.2);
            using (context.PushOpacity(0.55 + 0.25 * flicker)) context.FillRectangle(WindowGlow, glow);
            return;
        }
        // The lantern hangs at the street-side corner of the door.
        var lx = cell.X + (part.StreetX != 0 ? (part.StreetX > 0 ? w * 0.92 : w * 0.08) : w * 0.86);
        var ly = cell.Y + (part.StreetY != 0 ? (part.StreetY > 0 ? h * 0.9 : h * 0.1) : h * 0.18);
        var r = w * (1.05 + 0.15 * flicker);
        context.FillRectangle(SconceGlow, new Rect(lx - r, ly - r, r * 2, r * 2));
        context.FillRectangle(SconceBrass, new Rect(lx - w * 0.07, ly - h * 0.1, w * 0.14, h * 0.2));
        context.DrawEllipse(SconceFlame, null, new Point(lx, ly), w * 0.05, h * (0.07 + 0.02 * flicker));
    }

    /// <summary>Each shop's name, on a small board over its roof just behind the door, in its colour.</summary>
    private static void DrawShopSigns(DrawingContext context, IMapSource source, int offsetX, int offsetY, int endX, int endY,
        double originX, double originY, Size cell)
    {
        var signs = source.ShopSigns;
        if (signs.Count == 0) return;
        var size = Math.Clamp(cell.Height * 0.42, 10, 18);
        foreach (var sign in signs)
        {
            if (sign.Door.X < offsetX - 4 || sign.Door.X >= endX + 4 || sign.Door.Y < offsetY - 2 || sign.Door.Y >= endY + 2) continue;
            var text = new FormattedText(sign.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                SignFace, size, SignText);
            // Over the roof, clear of the door: back from the street by a square and a bit (and, beside a door
            // that faces sideways, by half the board's width too).
            var cx = originX + (sign.Door.X - offsetX + 0.5) * cell.Width - sign.StreetX * (cell.Width * 0.9 + text.Width / 2 + 6);
            var cy = originY + (sign.Door.Y - offsetY + 0.5) * cell.Height - sign.StreetY * (cell.Height * 0.75 + text.Height / 2 + 2);
            var board = new Rect(cx - text.Width / 2 - 6, cy - text.Height / 2 - 2, text.Width + 12, text.Height + 4);
            context.DrawRectangle(SignBoard, new Pen(ShopBrush(sign.Colour), 1.5), board, 3, 3);
            context.DrawText(text, new Point(board.X + 6, board.Y + 2));
        }
    }

    private static readonly Typeface SignFace = new("avares://AVABand/Assets/Fonts#Cinzel");
    private static readonly IBrush SignBoard = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0xE6, 0x1C, 0x14, 0x0C));
    private static readonly IBrush SignText = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xF3, 0xE6, 0xC4));

    private static void DrawSconce(DrawingContext context, Rect cell, double flicker)
    {
        var (w, h) = (cell.Width, cell.Height);
        var cx = cell.X + w / 2;
        var flameY = cell.Y + h * 0.36;
        context.DrawEllipse(SconceGlow, null, new Point(cx, flameY), w * 0.48, h * 0.46);
        context.FillRectangle(SconceBrass, new Rect(cx - w * 0.14, cell.Y + h * 0.58, w * 0.28, Math.Max(1, h * 0.09)));   // the bracket
        context.FillRectangle(SconceBrass, new Rect(cx - w * 0.05, cell.Y + h * 0.48, w * 0.10, h * 0.12));                 // its cup
        context.DrawEllipse(SconceFlame, null, new Point(cx, flameY), w * 0.09, h * 0.14 * (0.85 + 0.15 * flicker));
        context.DrawEllipse(SconceCore, null, new Point(cx, flameY + h * 0.03), w * 0.045, h * 0.07);
    }

    // Torchlight flicker: a slow clock (10 a second) runs only while torchlit squares are shown.
    private global::Avalonia.Threading.DispatcherTimer? _flickerClock;
    private readonly System.Diagnostics.Stopwatch _flickerWatch = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>1 give or take a little, wavering as a flame does.</summary>
    private double Flicker()
    {
        var t = _flickerWatch.Elapsed.TotalSeconds;
        return 1 + 0.07 * Math.Sin(t * 9.1) + 0.04 * Math.Sin(t * 23.7 + 1.1);
    }

    private void UpdateFlickerClock(bool needed)
    {
        if (needed && _flickerClock is null)
        {
            _flickerClock = new global::Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(100),
                global::Avalonia.Threading.DispatcherPriority.Background, (_, _) => InvalidateVisual());
            _flickerClock.Start();
        }
        else if (!needed && _flickerClock is not null)
        {
            _flickerClock.Stop();
            _flickerClock = null;
        }
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
