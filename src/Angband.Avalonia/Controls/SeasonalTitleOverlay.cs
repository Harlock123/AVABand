using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Angband.Avalonia.Controls;

/// <summary>
/// What a seasonal event paints over the title picture while it runs (AVABand's seasonal events): for
/// All Hallows' Eve, a harvest moon with bats across it, a dusk of purple and orange, and carved pumpkins
/// grinning on the plain; for Midwinter, a cold wash, a pale moon, falling snow, drifts along the foot
/// and a sprig of holly in the corner. Nothing for any other value of <see cref="Season"/> (or none).
/// </summary>
public sealed class SeasonalTitleOverlay : Control
{
    public static readonly StyledProperty<string?> SeasonProperty =
        AvaloniaProperty.Register<SeasonalTitleOverlay, string?>(nameof(Season));

    /// <summary>The event's id (<see cref="Angband.Core.Game.SeasonDef.Id"/>), or null for none.</summary>
    public string? Season
    {
        get => GetValue(SeasonProperty);
        set => SetValue(SeasonProperty, value);
    }

    static SeasonalTitleOverlay() => AffectsRender<SeasonalTitleOverlay>(SeasonProperty);

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        switch (Season)
        {
            case "halloween": PaintHalloween(context, w, h); break;
            case "midwinter": PaintMidwinter(context, w, h); break;
        }
    }

    /// <summary>A fixed scatter (the same every time): a small linear-congruential sequence in 0..1.</summary>
    private static IEnumerable<double> Scatter(uint seed)
    {
        var state = seed;
        while (true)
        {
            state = state * 1664525 + 1013904223;
            yield return (state >> 8) / (double)(1 << 24);
        }
    }

    private static RadialGradientBrush Glow(Point centre, double radius, Color inner, Color outer) => new()
    {
        Center = new RelativePoint(centre, RelativeUnit.Absolute),
        GradientOrigin = new RelativePoint(centre, RelativeUnit.Absolute),
        RadiusX = new RelativeScalar(radius, RelativeUnit.Absolute),
        RadiusY = new RelativeScalar(radius, RelativeUnit.Absolute),
        GradientStops = { new GradientStop(inner, 0), new GradientStop(outer, 1) },
    };

    private static LinearGradientBrush Wash(params (double At, Color Colour)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        foreach (var (at, colour) in stops) brush.GradientStops.Add(new GradientStop(colour, at));
        return brush;
    }

    // ---- All Hallows' Eve ----

    private static void PaintHalloween(DrawingContext context, double w, double h)
    {
        var u = Math.Min(w, h * 16 / 9) / 100; // one unit: a hundredth of the picture's width
        // A purple dusk above, a warmer glow on the plain.
        context.FillRectangle(Wash((0, Color.FromArgb(110, 46, 12, 70)), (0.55, Color.FromArgb(40, 90, 30, 60)), (1, Color.FromArgb(60, 140, 50, 0))),
            new Rect(0, 0, w, h));

        // The harvest moon, low and huge, top right, with its halo.
        var moon = new Point(w * 0.865, h * 0.21);
        var r = 6.4 * u;
        context.DrawEllipse(Glow(moon, r * 3.2, Color.FromArgb(90, 255, 150, 50), Color.FromArgb(0, 255, 120, 30)), null, moon, r * 3.2, r * 3.2);
        context.DrawEllipse(Glow(new Point(moon.X - r * 0.3, moon.Y - r * 0.3), r * 1.3, Color.FromRgb(255, 226, 150), Color.FromRgb(236, 140, 42)),
            null, moon, r, r);
        // Its seas, faintly.
        var sea = new SolidColorBrush(Color.FromArgb(46, 150, 70, 10));
        context.DrawEllipse(sea, null, new Point(moon.X + r * 0.25, moon.Y - r * 0.2), r * 0.3, r * 0.22);
        context.DrawEllipse(sea, null, new Point(moon.X - r * 0.3, moon.Y + r * 0.3), r * 0.22, r * 0.16);
        context.DrawEllipse(sea, null, new Point(moon.X + r * 0.1, moon.Y + r * 0.45), r * 0.16, r * 0.12);

        // Bats across the moon and the sky.
        var bat = new SolidColorBrush(Color.FromRgb(14, 6, 12));
        (double X, double Y, double Size)[] bats =
        [
            (0.84, 0.18, 1.5), (0.9, 0.26, 1.0), (0.79, 0.31, 0.8), (0.93, 0.12, 0.7), (0.74, 0.36, 0.55), (0.97, 0.34, 0.55),
            (0.14, 0.12, 0.6), (0.2, 0.2, 0.45), (0.1, 0.27, 0.4),
        ];
        foreach (var (x, y, size) in bats) DrawBat(context, bat, new Point(w * x, h * y), size * 1.6 * u);

        // Jack-o'-lanterns on the plain, bottom right, away from the menu.
        DrawPumpkin(context, new Point(w * 0.80, h * 0.9), 4.6 * u, 0);
        DrawPumpkin(context, new Point(w * 0.885, h * 0.93), 3.4 * u, 1);
        DrawPumpkin(context, new Point(w * 0.73, h * 0.945), 2.6 * u, 2);
    }

    /// <summary>A bat, wings spread, its body at <paramref name="at"/>, <paramref name="s"/> across each wing.</summary>
    private static void DrawBat(DrawingContext context, IBrush brush, Point at, double s)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(at.X, at.Y - s * 0.15), true);
            // Right wing: up to its tip, then the scalloped trailing edge back to the body.
            c.QuadraticBezierTo(new Point(at.X + s * 0.6, at.Y - s * 0.65), new Point(at.X + s * 1.6, at.Y - s * 0.35));
            c.QuadraticBezierTo(new Point(at.X + s * 1.35, at.Y + s * 0.05), new Point(at.X + s * 1.05, at.Y + s * 0.1));
            c.QuadraticBezierTo(new Point(at.X + s * 0.85, at.Y - s * 0.12), new Point(at.X + s * 0.62, at.Y + s * 0.12));
            c.QuadraticBezierTo(new Point(at.X + s * 0.4, at.Y - s * 0.06), new Point(at.X + s * 0.18, at.Y + s * 0.28));
            // The body and ears.
            c.LineTo(new Point(at.X, at.Y + s * 0.38));
            c.LineTo(new Point(at.X - s * 0.18, at.Y + s * 0.28));
            // Left wing, mirrored.
            c.QuadraticBezierTo(new Point(at.X - s * 0.4, at.Y - s * 0.06), new Point(at.X - s * 0.62, at.Y + s * 0.12));
            c.QuadraticBezierTo(new Point(at.X - s * 0.85, at.Y - s * 0.12), new Point(at.X - s * 1.05, at.Y + s * 0.1));
            c.QuadraticBezierTo(new Point(at.X - s * 1.35, at.Y + s * 0.05), new Point(at.X - s * 1.6, at.Y - s * 0.35));
            c.QuadraticBezierTo(new Point(at.X - s * 0.6, at.Y - s * 0.65), new Point(at.X - s * 0.08, at.Y - s * 0.15));
            c.LineTo(new Point(at.X - s * 0.12, at.Y - s * 0.32));
            c.LineTo(new Point(at.X, at.Y - s * 0.2));
            c.LineTo(new Point(at.X + s * 0.12, at.Y - s * 0.32));
            c.EndFigure(true);
        }
        context.DrawGeometry(brush, null, g);
    }

    /// <summary>A carved pumpkin sitting at <paramref name="at"/> (its base), <paramref name="r"/> in radius, lit from inside.</summary>
    private static void DrawPumpkin(DrawingContext context, Point at, double r, int face)
    {
        var centre = new Point(at.X, at.Y - r * 0.8);
        // Its light on the ground.
        context.DrawEllipse(Glow(centre, r * 3, Color.FromArgb(110, 255, 140, 30), Color.FromArgb(0, 255, 110, 20)), null, centre, r * 3, r * 3);
        // The lobes, darkest at the sides.
        var rind = new Pen(new SolidColorBrush(Color.FromRgb(120, 48, 6)), Math.Max(1, r * 0.05));
        foreach (var (dx, rx, shade) in new[] { (-0.55, 0.62, 0.72), (0.55, 0.62, 0.72), (-0.22, 0.6, 0.88), (0.22, 0.6, 0.88), (0.0, 0.55, 1.0) })
        {
            var colour = Color.FromRgb((byte)(226 * shade), (byte)(112 * shade), (byte)(18 * shade));
            context.DrawEllipse(new SolidColorBrush(colour), rind, new Point(centre.X + dx * r, centre.Y), rx * r, r * 0.8);
        }
        // The stalk.
        var stalk = new StreamGeometry();
        using (var c = stalk.Open())
        {
            c.BeginFigure(new Point(centre.X - r * 0.1, centre.Y - r * 0.72), true);
            c.QuadraticBezierTo(new Point(centre.X - r * 0.05, centre.Y - r * 1.05), new Point(centre.X + r * 0.18, centre.Y - r * 1.12));
            c.LineTo(new Point(centre.X + r * 0.2, centre.Y - r * 1.0));
            c.QuadraticBezierTo(new Point(centre.X + r * 0.08, centre.Y - r * 0.95), new Point(centre.X + r * 0.1, centre.Y - r * 0.72));
            c.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(Color.FromRgb(70, 82, 28)), null, stalk);

        // The face, glowing.
        var light = Glow(centre, r * 0.9, Color.FromRgb(255, 240, 150), Color.FromRgb(255, 170, 40));
        var faceGeometry = new StreamGeometry();
        using (var c = faceGeometry.Open())
        {
            void Poly(params (double X, double Y)[] pts)
            {
                c.BeginFigure(new Point(centre.X + pts[0].X * r, centre.Y + pts[0].Y * r), true);
                foreach (var p in pts.Skip(1)) c.LineTo(new Point(centre.X + p.X * r, centre.Y + p.Y * r));
                c.EndFigure(true);
            }
            // Eyes: triangles (or slanted, for a meaner one), and a nose.
            if (face == 1)
            {
                Poly((-0.5, -0.32), (-0.14, -0.18), (-0.42, -0.06));
                Poly((0.5, -0.32), (0.14, -0.18), (0.42, -0.06));
            }
            else
            {
                Poly((-0.42, -0.08), (-0.28, -0.38), (-0.12, -0.08));
                Poly((0.12, -0.08), (0.28, -0.38), (0.42, -0.08));
            }
            Poly((-0.07, 0.06), (0.0, -0.06), (0.07, 0.06));
            // The grin: a crescent with teeth.
            var teeth = face == 2 ? 2 : 3;
            c.BeginFigure(new Point(centre.X - r * 0.52, centre.Y + r * 0.14), true);
            for (var i = 1; i <= teeth * 2; i++)
            {
                var t = i / (double)(teeth * 2);
                var x = -0.52 + 1.04 * t;
                var y = 0.14 + 0.06 * Math.Sin(t * Math.PI) + (i % 2 == 1 ? 0.12 : 0);
                c.LineTo(new Point(centre.X + x * r, centre.Y + y * r));
            }
            c.QuadraticBezierTo(new Point(centre.X, centre.Y + r * 0.78), new Point(centre.X - r * 0.52, centre.Y + r * 0.14));
            c.EndFigure(true);
        }
        context.DrawGeometry(light, null, faceGeometry);
    }

    // ---- Midwinter ----

    private static void PaintMidwinter(DrawingContext context, double w, double h)
    {
        var u = Math.Min(w, h * 16 / 9) / 100;
        // A cold wash: the fire's glow cooled to a winter dusk.
        context.FillRectangle(Wash((0, Color.FromArgb(120, 20, 40, 90)), (0.5, Color.FromArgb(110, 40, 70, 130)), (1, Color.FromArgb(90, 120, 150, 200))),
            new Rect(0, 0, w, h));

        // A pale moon, top right, in a ring of frost.
        var moon = new Point(w * 0.86, h * 0.21);
        var r = 4.6 * u;
        context.DrawEllipse(Glow(moon, r * 3.4, Color.FromArgb(80, 190, 220, 255), Color.FromArgb(0, 160, 200, 255)), null, moon, r * 3.4, r * 3.4);
        context.DrawEllipse(Glow(new Point(moon.X - r * 0.3, moon.Y - r * 0.3), r * 1.4, Color.FromRgb(250, 252, 255), Color.FromRgb(196, 214, 236)),
            null, moon, r, r);

        // Snow lying along the foot of the picture.
        var drift = new StreamGeometry();
        using (var c = drift.Open())
        {
            c.BeginFigure(new Point(0, h), true);
            c.LineTo(new Point(0, h * 0.93));
            c.CubicBezierTo(new Point(w * 0.15, h * 0.88), new Point(w * 0.3, h * 0.95), new Point(w * 0.48, h * 0.92));
            c.CubicBezierTo(new Point(w * 0.65, h * 0.89), new Point(w * 0.78, h * 0.85), new Point(w, h * 0.9));
            c.LineTo(new Point(w, h));
            c.EndFigure(true);
        }
        context.DrawGeometry(Wash((0, Color.FromArgb(235, 236, 242, 252)), (1, Color.FromArgb(255, 170, 186, 214))), null, drift);

        // Falling snow: a fixed scatter, bigger and brighter nearer.
        var rand = Scatter(0x5EA5).GetEnumerator();
        double Next()
        {
            rand.MoveNext();
            return rand.Current;
        }
        for (var i = 0; i < 240; i++)
        {
            var x = Next() * w;
            var y = Next() * h;
            var near = Next();
            var size = (0.08 + near * near * 0.3) * u;
            var alpha = (byte)(70 + near * 150);
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, 245, 250, 255)), null, new Point(x, y), size, size);
        }

        // Holly in the top left-hand corner (the moon has the other).
        DrawHolly(context, new Point(0, 0), 1, u * 0.8);
    }

    /// <summary>A sprig of holly from a corner (<paramref name="flip"/> -1 for a right-hand one): three leaves and a cluster of berries.</summary>
    private static void DrawHolly(DrawingContext context, Point corner, int flip, double u)
    {
        var leaf = new SolidColorBrush(Color.FromRgb(28, 92, 44));
        var vein = new Pen(new SolidColorBrush(Color.FromRgb(70, 140, 80)), Math.Max(1, u * 0.12));
        var berryAt = new Point(corner.X + flip * 7.5 * u, corner.Y + 6.5 * u);
        foreach (var angle in new[] { 200.0, 250.0, 320.0 })
        {
            var a = angle * Math.PI / 180;
            var dir = new Vector(Math.Cos(a) * flip, -Math.Sin(a));
            var tip = berryAt + dir * 8 * u;
            var side = new Vector(-dir.Y, dir.X) * 2.2 * u;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(berryAt, true);
                // A spiky edge on either side: points out, notches in.
                for (var i = 1; i <= 4; i++)
                {
                    var t = i / 4.0;
                    var bulge = Math.Sin(t * Math.PI) * (i % 2 == 1 ? 1.25 : 0.8);
                    c.LineTo(berryAt + (tip - berryAt) * t + side * bulge);
                }
                c.LineTo(tip);
                for (var i = 3; i >= 1; i--)
                {
                    var t = i / 4.0;
                    var bulge = Math.Sin(t * Math.PI) * (i % 2 == 1 ? 1.25 : 0.8);
                    c.LineTo(berryAt + (tip - berryAt) * t - side * bulge);
                }
                c.EndFigure(true);
            }
            context.DrawGeometry(leaf, null, g);
            context.DrawLine(vein, berryAt, tip);
        }
        foreach (var (dx, dy) in new[] { (0.0, 0.0), (1.3, 0.6), (0.2, 1.4) })
        {
            var at = new Point(berryAt.X + flip * dx * u, berryAt.Y + dy * u);
            context.DrawEllipse(Glow(new Point(at.X - 0.3 * u, at.Y - 0.3 * u), 1.1 * u, Color.FromRgb(240, 70, 60), Color.FromRgb(150, 10, 20)), null, at, 0.9 * u, 0.9 * u);
        }
    }
}
