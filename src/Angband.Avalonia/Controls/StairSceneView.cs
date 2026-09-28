using Angband.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Angband.Avalonia.Controls;

/// <summary>
/// The moment's scene on taking the stairs (<see cref="StairScene"/>), painted in one-point
/// perspective: going down, the steps fall away below the eye into the dark, lit by two flickering
/// torches; going up, they climb toward a pale light — warm by day, cold by night when the town is
/// above, dim grey in the dungeon. The steps drift toward you as you go, it fades in and out, and it
/// ends by itself after <see cref="DurationMs"/>. A picture of the player's own is shown instead
/// (slowly zooming), if the scene names one.
/// </summary>
public sealed class StairSceneView : Control
{
    public const double DurationMs = 1500;

    public static readonly StyledProperty<StairScene?> SceneProperty =
        AvaloniaProperty.Register<StairSceneView, StairScene?>(nameof(Scene));

    public StairScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>Raised when the scene has played through.</summary>
    public event Action? Finished;

    /// <summary>Milliseconds since the scene began.</summary>
    public double Elapsed { get; private set; }

    private global::Avalonia.Threading.DispatcherTimer? _clock;
    private readonly System.Diagnostics.Stopwatch _watch = new();
    private (string Path, Bitmap Bitmap)? _picture;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SceneProperty) return;
        Elapsed = 0;
        if (Scene is null)
        {
            _clock?.Stop();
            return;
        }
        _clock ??= new global::Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(16),
            global::Avalonia.Threading.DispatcherPriority.Render, (_, _) => Tick());
        _watch.Restart();
        _clock.Start();
        InvalidateVisual();
    }

    private void Tick()
    {
        var ms = _watch.Elapsed.TotalMilliseconds;
        _watch.Restart();
        Advance(ms);
    }

    /// <summary>Moves the scene on (the view's clock does this; tests call it directly).</summary>
    public void Advance(double ms)
    {
        if (Scene is null) return;
        Elapsed += ms;
        InvalidateVisual();
        if (Elapsed < DurationMs) return;
        _clock?.Stop();
        Finished?.Invoke();
    }

    public override void Render(DrawingContext context)
    {
        if (Scene is not { } scene || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var p = Math.Clamp(Elapsed / DurationMs, 0, 1);
        var fade = p < 0.18 ? p / 0.18 : p > 0.8 ? (1 - p) / 0.2 : 1;
        using (context.PushOpacity(Math.Clamp(fade, 0, 1)))
        {
            context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            if (scene.Picture is { } path && Picture(path) is { } bitmap) DrawPicture(context, bitmap, p);
            else DrawStairwell(context, scene, p);
            DrawCaption(context, scene.Caption);
        }
    }

    private Bitmap? Picture(string path)
    {
        if (_picture is { } cached && cached.Path == path) return cached.Bitmap;
        try
        {
            var bitmap = new Bitmap(path);
            _picture = (path, bitmap);
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null; // not a picture after all: the painted scene instead
        }
    }

    /// <summary>The player's own picture, filling the view and zooming in a little as it plays.</summary>
    private void DrawPicture(DrawingContext context, Bitmap bitmap, double p)
    {
        var view = Bounds.Size;
        var src = bitmap.Size;
        var scale = Math.Max(view.Width / src.Width, view.Height / src.Height) * (1 + 0.08 * p);
        var size = new Size(src.Width * scale, src.Height * scale);
        var dest = new Rect(new Point((view.Width - size.Width) / 2, (view.Height - size.Height) / 2), size);
        context.DrawImage(bitmap, new Rect(src), dest);
    }

    private void DrawCaption(DrawingContext context, string caption)
    {
        var size = Math.Max(14, Bounds.Height / 22);
        var text = new FormattedText(caption, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Italic, FontWeight.SemiBold), size, Parchment);
        // A band of dark behind it, so it reads over any scene or picture.
        var band = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Color.FromArgb(210, 0, 0, 0), 0.45) },
        };
        context.FillRectangle(band, new Rect(0, Bounds.Height * 0.80, Bounds.Width, Bounds.Height * 0.20));
        var at = new Point((Bounds.Width - text.Width) / 2, Bounds.Height * 0.89);
        var shadow = new FormattedText(caption, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Italic, FontWeight.SemiBold), size, Brushes.Black);
        context.DrawText(shadow, at + new Point(2, 2));
        context.DrawText(text, at);
    }

    private static readonly IBrush Parchment = new SolidColorBrush(Color.FromRgb(0xE2, 0xD3, 0xAE));

    // --- The painted stairwell ------------------------------------------------------------------

    private const int Steps = 16;

    /// <summary>How far away a step is drawn (0 at the eye), given its place and how far the scene has gone.</summary>
    private static double Depth(int k, double p) => (k + 1 - (p * 2.4 % 1)) * 0.42;

    private void DrawStairwell(DrawingContext context, StairScene scene, double p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var cx = w / 2;
        var down = scene.Down;
        var vy = h * (down ? 0.62 : 0.30);  // the vanishing point: below the eye going down, above going up
        var nearHalf = w * 0.40;
        double Scale(double d) => 1 / (1 + d);
        double FloorY(double d) => vy + (h * 1.02 - vy) * Scale(d);
        double CeilY(double d) => vy - (vy + h * 0.25) * Scale(d);
        double Half(double d) => nearHalf * Scale(d);

        var t = p * DurationMs / 1000;
        var flicker = 0.86 + 0.08 * Math.Sin(t * 17.3) + 0.06 * Math.Sin(t * 7.1 + 1.3);
        var (lightColour, lightStrength) = down ? (Color.FromRgb(255, 150, 60), 0.0)
            : scene.Depth == 0 ? scene.Day ? (Color.FromRgb(255, 238, 200), 1.0) : (Color.FromRgb(150, 170, 235), 0.8)
            : (Color.FromRgb(200, 208, 225), 0.55);

        // The far end: black going down, the light going up.
        var farD = Depth(Steps, 0) + 1;
        var far = new Rect(cx - Half(farD), CeilY(farD), 2 * Half(farD), FloorY(farD) - CeilY(farD));
        if (!down)
        {
            context.FillRectangle(new SolidColorBrush(lightColour, lightStrength), far);
        }

        // Walls and ceiling: dark stone, their mortar lines running to the vanishing point.
        var stone = down ? Color.FromRgb(58, 52, 46) : Color.FromRgb(52, 55, 62);
        Polygon(context, Shade(stone, 0.55), new(0, 0), new(far.Left, far.Top), new(far.Left, far.Bottom), new(0, h));
        Polygon(context, Shade(stone, 0.55), new(w, 0), new(far.Right, far.Top), new(far.Right, far.Bottom), new(w, h));
        Polygon(context, Shade(stone, 0.35), new(0, 0), new(w, 0), new(far.Right, far.Top), new(far.Left, far.Top));
        var mortar = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1.2);
        for (var i = 1; i < 7; i++)
        {
            var y = h * i / 7.0;
            var fy = far.Top + (far.Bottom - far.Top) * i / 7.0;
            context.DrawLine(mortar, new Point(0, y), new Point(far.Left, fy));
            context.DrawLine(mortar, new Point(w, y), new Point(far.Right, fy));
        }

        // The steps, far to near: treads catch the light, risers are darker; nosings are picked out.
        for (var k = Steps - 1; k >= 0; k--)
        {
            var d0 = Depth(k, p);
            var d1 = d0 + 0.42;
            var light = down
                ? Math.Clamp(1.15 - d0 / 3.2, 0, 1) * flicker                        // torchlight from behind you
                : Math.Clamp(0.25 + 0.75 * (d0 / (d0 + 2.2)) * (0.6 + 0.4 * lightStrength), 0, 1); // light from above
            var nearY = FloorY(d0);
            var farY = FloorY(d1);
            var riser = (farY - nearY) * (down ? 0.25 : 0.55);
            var tread = Shade(stone, 0.5 + 1.3 * light);
            var face = Shade(stone, 0.3 + 0.8 * light);
            // Going up the riser faces you, below the tread; going down you see only the tread's edge.
            var topY = down ? nearY : nearY + riser;
            Polygon(context, tread, new(cx - Half(d0), topY), new(cx + Half(d0), topY), new(cx + Half(d1), farY), new(cx - Half(d1), farY));
            if (!down)
                context.FillRectangle(face, new Rect(cx - Half(d0), nearY, 2 * Half(d0), Math.Max(0, riser)));
            var nosing = new Pen(new SolidColorBrush(Color.FromArgb((byte)(210 * light), 235, 220, 190)), Math.Max(1, 3 * Scale(d0)));
            if (down)
            {
                // Going down, each step's far edge is a lip, and the next step starts below it: the
                // near part of every tread lies in the shadow of the step above.
                var shadowTop = nearY - (nearY - farY) * 0.45;
                Polygon(context, new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
                    new(cx - Half(d0), nearY), new(cx + Half(d0), nearY),
                    new(cx + Half(d0 + 0.42 * 0.45), shadowTop), new(cx - Half(d0 + 0.42 * 0.45), shadowTop));
                context.DrawLine(nosing, new Point(cx - Half(d1), farY), new Point(cx + Half(d1), farY));
            }
            else context.DrawLine(nosing, new Point(cx - Half(d0), topY), new Point(cx + Half(d0), topY));
            // The wall joints at this step.
            var joint = new Pen(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), 1);
            context.DrawLine(joint, new Point(cx - Half(d0), CeilY(d0)), new Point(cx - Half(d0), topY));
            context.DrawLine(joint, new Point(cx + Half(d0), CeilY(d0)), new Point(cx + Half(d0), topY));
        }

        if (down)
        {
            // Two torches in their brackets, flickering, and the dark swallowing the far steps.
            foreach (var side in new[] { -1, 1 })
            {
                var torch = new Point(cx + side * nearHalf * 0.93, h * 0.40);
                var glow = new RadialGradientBrush
                {
                    Center = new RelativePoint(torch, RelativeUnit.Absolute),
                    GradientOrigin = new RelativePoint(torch, RelativeUnit.Absolute),
                    RadiusX = new RelativeScalar(w * 0.34 * flicker, RelativeUnit.Absolute),
                    RadiusY = new RelativeScalar(w * 0.34 * flicker, RelativeUnit.Absolute),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb((byte)(120 * flicker), lightColour.R, lightColour.G, lightColour.B), 0),
                        new GradientStop(Color.FromArgb(0, lightColour.R, lightColour.G, lightColour.B), 1),
                    },
                };
                context.FillRectangle(glow, new Rect(Bounds.Size));
                var r = h * 0.012;
                context.DrawEllipse(new SolidColorBrush(Color.FromArgb(160, 255, 120, 40)), null, torch, r * 2.2 * flicker, r * 3.4 * flicker);
                context.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 214, 130)), null, torch, r * flicker, r * 2 * flicker);
                context.FillRectangle(new SolidColorBrush(Color.FromRgb(46, 34, 24)), new Rect(torch.X - r * 0.6, torch.Y + r * 1.6, r * 1.2, r * 6));
                context.FillRectangle(new SolidColorBrush(Color.FromRgb(30, 26, 24)), new Rect(torch.X - r * 1.4, torch.Y + r * 4.5, r * 2.8, r * 0.8));
            }
            var gloom = new RadialGradientBrush
            {
                Center = new RelativePoint(cx, vy, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(cx, vy, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(w * 0.28, RelativeUnit.Absolute),
                RadiusY = new RelativeScalar(h * 0.30, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(Color.FromArgb(255, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1) },
            };
            context.FillRectangle(gloom, new Rect(Bounds.Size));
        }
        else
        {
            // The light spilling down from the top of the stairs.
            var spill = new RadialGradientBrush
            {
                Center = new RelativePoint(cx, far.Center.Y, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(cx, far.Center.Y, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(w * 0.45, RelativeUnit.Absolute),
                RadiusY = new RelativeScalar(h * 0.55, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(150 * lightStrength), lightColour.R, lightColour.G, lightColour.B), 0),
                    new GradientStop(Color.FromArgb(0, lightColour.R, lightColour.G, lightColour.B), 1),
                },
            };
            context.FillRectangle(spill, new Rect(Bounds.Size));
        }
    }

    private static IBrush Shade(Color c, double f) =>
        new SolidColorBrush(Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255)));

    private static void Polygon(DrawingContext context, IBrush fill, params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], true);
            foreach (var pt in points.Skip(1)) g.LineTo(pt);
            g.EndFigure(true);
        }
        context.DrawGeometry(fill, null, geometry);
    }
}
