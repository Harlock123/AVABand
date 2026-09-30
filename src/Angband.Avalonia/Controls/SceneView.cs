using Angband.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Angband.Avalonia.Controls;

/// <summary>
/// The moment's scene on taking the stairs (<see cref="AmbientScene"/>), painted in one-point
/// perspective: going down, the steps fall away below the eye into the dark, lit by two flickering
/// torches; going up, they climb toward a pale light — warm by day, cold by night when the town is
/// above, dim grey in the dungeon. The steps drift toward you as you go, it fades in and out, and it
/// ends by itself after <see cref="DurationMs"/>. A picture of the player's own is shown instead
/// (slowly zooming), if the scene names one.
/// </summary>
public sealed class SceneView : Control
{
    public const double DurationMs = 1500;

    /// <summary>How long a scene lasts: death and a first unique a little longer.</summary>
    public static double DurationFor(SceneKind kind) => kind switch
    {
        SceneKind.Death => 3200,
        SceneKind.Unique => 2200,
        SceneKind.RecallUp or SceneKind.RecallDown => 3600,
        SceneKind.DeepDescent or SceneKind.Trapdoor => 1800,
        SceneKind.QuestComplete or SceneKind.BossSlain => 2600,
        _ => DurationMs,
    };

    private double Duration => Scene is { } s ? DurationFor(s.Kind) : DurationMs;

    /// <summary>Where a scene that waits for Space holds: faded in, before it would fade out.</summary>
    public const double HoldAt = 0.8;

    /// <summary>Where a scene holds: the recall portal before its hand has closed, the rest at <see cref="HoldAt"/>.</summary>
    public static double HoldFor(AmbientScene scene) => scene.PortalArt is not null ? RecallHold : HoldAt;

    /// <summary>Whether the scene is holding for Space (it has played up to <see cref="HoldFor"/>).</summary>
    public bool IsHolding => Scene is { WaitForKey: true } s && Elapsed >= Duration * HoldFor(s);

    public static readonly StyledProperty<AmbientScene?> SceneProperty =
        AvaloniaProperty.Register<SceneView, AmbientScene?>(nameof(Scene));

    public AmbientScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>Raised when the scene has played through.</summary>
    public event Action? Finished;

    /// <summary>Raised with a sound's name (scene-sounds/&lt;name&gt;.ogg) when the scene reaches it.</summary>
    public event Action<string>? Cue;

    /// <summary>
    /// The sounds a scene plays, and when (as a fraction of it): the recall cutscene's rent tearing
    /// and the hand reaching at the start, then its grasp and thunderclap (timed to the flash) —
    /// never reached while the scene holds for Space; footsteps on the stairs; a bell at death; a
    /// swelling chord for a first unique; a rumble for a deadly level.
    /// </summary>
    public static IReadOnlyList<(double At, string Sound)> CuesFor(SceneKind kind) => kind switch
    {
        SceneKind.RecallUp or SceneKind.RecallDown => [(0, "recall-reach"), (0.62, "recall-take")],
        SceneKind.StairsDown => [(0, "stairs-down")],
        SceneKind.StairsUp => [(0, "stairs-up")],
        SceneKind.Death => [(0, "death")],
        SceneKind.DeepDescent or SceneKind.Trapdoor => [(0, "fall")],
        SceneKind.QuestComplete => [(0, "quest-complete")],
        SceneKind.BossSlain => [(0, "boss-slain")],
        SceneKind.Unique => [(0, "unique")],
        SceneKind.Danger => [(0, "danger")],
        _ => [],
    };

    /// <summary>Plays the cues passed between two moments (ms); from &lt; 0 is the scene's start.</summary>
    private void FireCues(double from, double to)
    {
        if (Scene is not { } scene) return;
        foreach (var (at, sound) in CuesFor(scene.Kind))
            if (at == 0 ? from < 0 : at * Duration > from && at * Duration <= to) Cue?.Invoke(sound);
    }

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
        FireCues(-1, 0);
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
        var before = Elapsed;
        Elapsed += ms;
        InvalidateVisual();
        if (Scene.WaitForKey) Elapsed = Math.Min(Elapsed, Duration * HoldFor(Scene));
        if (Elapsed > before) FireCues(before, Elapsed);
        if (Scene.WaitForKey && Elapsed >= Duration * HoldFor(Scene))
        {
            // Hold, faded in, on this frame until dismissed (the view model ends it).
            Elapsed = Duration * HoldFor(Scene);
            _clock?.Stop();
            return;
        }
        if (Elapsed < Duration) return;
        _clock?.Stop();
        Finished?.Invoke();
    }

    public override void Render(DrawingContext context)
    {
        if (Scene is not { } scene || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var p = Math.Clamp(Elapsed / Duration, 0, 1);
        var fade = p < 0.18 ? p / 0.18 : p > 0.8 ? (1 - p) / 0.2 : 1;
        using (context.PushOpacity(Math.Clamp(fade, 0, 1)))
        {
            context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            if (scene.Picture is { } path && Picture(path) is { } bitmap)
            {
                var falling = scene.Kind is SceneKind.DeepDescent or SceneKind.Trapdoor;
                var dest = DrawPicture(context, bitmap, p, falling ? 0.45 * p * p : 0.08 * p);
                if (scene.PortalArt is { } art) DrawRecallPortal(context, dest, art, p);
                DrawPictureMotion(context, scene.Kind, p);
            }
            else Paint(context, scene, p);
            DrawCaption(context, scene.Caption, scene.Subtitle);
            if (IsHolding) DrawPrompt(context);
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

    /// <summary>The picture, filling the view and zooming in a little as it plays; returns where it went.</summary>
    private Rect DrawPicture(DrawingContext context, Bitmap bitmap, double p, double zoom)
    {
        var view = Bounds.Size;
        var src = bitmap.Size;
        var scale = Math.Max(view.Width / src.Width, view.Height / src.Height) * (1 + zoom);
        var size = new Size(src.Width * scale, src.Height * scale);
        var dest = new Rect(new Point((view.Width - size.Width) / 2, (view.Height - size.Height) / 2), size);
        context.DrawImage(bitmap, new Rect(src), dest);
        return dest;
    }

    /// <summary>
    /// What moves over the pictures of the newer scenes: falling, rubble streams past and out of the
    /// view and it shudders; a quest done, gold motes rise; a great foe slain, dust turns in the light.
    /// </summary>
    private void DrawPictureMotion(DrawingContext context, SceneKind kind, double p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var t = Elapsed / 1000;
        switch (kind)
        {
            case SceneKind.DeepDescent or SceneKind.Trapdoor:
                foreach (var (x, y, z) in Scatter(28, kind == SceneKind.Trapdoor ? 51 : 52))
                {
                    // From near the middle outward, faster and bigger as they pass: you fall past them.
                    var a = x * Math.PI * 2;
                    var r = (y + p * (1.2 + z)) % 1;
                    var at = new Point(w / 2 + Math.Cos(a) * w * 0.6 * r * r, h / 2 + Math.Sin(a) * h * 0.6 * r * r);
                    var size = 2 + 26 * r * r * (0.4 + z);
                    var colour = kind == SceneKind.DeepDescent ? Color.FromArgb((byte)(200 * r), 60, 40, 30) : Color.FromArgb((byte)(200 * r), 70, 50, 34);
                    context.DrawEllipse(new SolidColorBrush(colour), null, at, size, size * 0.7);
                }
                if (p < 0.3) // the jolt as the floor goes
                {
                    var jolt = (0.3 - p) / 0.3;
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(60 * jolt * Math.Abs(Math.Sin(t * 40))), 0, 0, 0)), new Rect(Bounds.Size));
                }
                break;
            case SceneKind.QuestComplete:
                foreach (var (x, y, z) in Scatter(50, 53))
                {
                    var rise = (y + p * (0.5 + z)) % 1;
                    var at = new Point(w * (0.15 + 0.7 * x) + Math.Sin(t * 2 + x * 9) * 12, h * (1 - rise));
                    var glint = 0.5 + 0.5 * Math.Sin(t * 5 + z * 20);
                    context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(90 + 140 * glint * (1 - rise)), 255, 214, 120)), null, at,
                        1 + 2.5 * z, 1 + 2.5 * z);
                }
                break;
            case SceneKind.BossSlain:
                foreach (var (x, y, z) in Scatter(60, 54))
                {
                    // Motes in the shaft of light, turning slowly.
                    var sway = Math.Sin(t * (0.4 + z) + x * 12);
                    var at = new Point(w * (0.44 + 0.12 * x) + sway * 18 * (0.4 + y), h * (0.08 + 0.72 * ((y + p * 0.12 * (z - 0.5) + 1) % 1)));
                    context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(60 + 120 * z), 255, 250, 235)), null, at, 0.8 + 1.6 * z, 0.8 + 1.6 * z);
                }
                break;
        }
    }

    // --- Word of Recall: the rent and the hand ------------------------------------------------------

    /// <summary>The rent in the backdrops (tools/recall_portal_art.py's PORTAL): centre and radii, as fractions of the picture.</summary>
    public const double PortalX = 0.5, PortalY = 0.445, PortalRx = 0.094, PortalRy = 0.24;

    /// <summary>Where a recall scene holds for Space: the hand reaching, not yet closed.</summary>
    public const double RecallHold = 0.6;

    private readonly Dictionary<string, Bitmap?> _art = new(StringComparer.Ordinal);

    private Bitmap? Art(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        if (_art.TryGetValue(path, out var cached)) return cached;
        Bitmap? bitmap = null;
        try { bitmap = new Bitmap(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return _art[path] = bitmap;
    }

    private static double Smooth(double a, double b, double x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// The Word of Recall cutscene, over its backdrop (a dungeon room, or the town square): the rent
    /// tears open, shimmering and crackling; a vast incorporeal hand reaches out of it toward you,
    /// growing until it fills the view, closes — and the light takes you.
    /// </summary>
    private void DrawRecallPortal(DrawingContext context, Rect dest, string art, double p)
    {
        var t = Elapsed / 1000;
        var centre = new Point(dest.X + dest.Width * PortalX, dest.Y + dest.Height * PortalY);
        var open = Smooth(0.02, 0.24, p);
        var rx = dest.Width * PortalRx * (0.15 + 0.85 * open);
        var ry = dest.Height * PortalRy * (0.15 + 0.85 * open);

        // Inside the rent: a swirl of violet light, turning, with ripples running out and motes circling.
        using (context.PushGeometryClip(new EllipseGeometry(new Rect(centre.X - rx, centre.Y - ry, rx * 2, ry * 2))))
        {
            var pulse = 0.85 + 0.15 * Math.Sin(t * 5.3);
            var core = new RadialGradientBrush
            {
                Center = new RelativePoint(centre, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(centre, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(rx, RelativeUnit.Absolute),
                RadiusY = new RelativeScalar(ry, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(255, 240, 228, 255), 0),
                    new GradientStop(Color.FromArgb(255, (byte)(150 * pulse), (byte)(95 * pulse), 235), 0.35),
                    new GradientStop(Color.FromArgb(255, 45, 16, 90), 1),
                },
            };
            context.FillRectangle(core, new Rect(centre.X - rx, centre.Y - ry, rx * 2, ry * 2));
            for (var arm = 0; arm < 6; arm++)
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    for (var i = 0; i <= 40; i++)
                    {
                        var r = i / 40.0;
                        var a = arm * Math.PI / 3 + t * 1.7 + r * 4.4;
                        var pt = new Point(centre.X + Math.Cos(a) * rx * r, centre.Y + Math.Sin(a) * ry * r);
                        if (i == 0) g.BeginFigure(pt, false);
                        else g.LineTo(pt);
                    }
                    g.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(80, 225, 200, 255)), Math.Max(2, rx * 0.06)), geometry);
            }
            for (var i = 0; i < 4; i++)
            {
                var phase = (t * 0.55 + i / 4.0) % 1;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(130 * (1 - phase)), 235, 220, 255)), 1.5 + 3 * (1 - phase));
                context.DrawEllipse(null, pen, centre, rx * phase, ry * phase);
            }
            foreach (var (x, y, z) in Scatter(40, 31))
            {
                var a = x * Math.PI * 2 + t * (0.6 + z);
                var r = 0.2 + 0.75 * y;
                context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(140 + 110 * z), 255, 245, 255)), null,
                    new Point(centre.X + Math.Cos(a) * rx * r, centre.Y + Math.Sin(a) * ry * r), 1 + 2 * z, 1 + 2 * z);
            }
        }

        // Its torn edge and the cracks running out from it, flickering.
        if (Art(art, "recall-portal-rim.png") is { } rim)
        {
            var w = rx * 2 * 1.9;
            var h = ry * 2 * 1.5;
            using (context.PushOpacity(Math.Clamp(open * (0.85 + 0.15 * Math.Sin(t * 11.7) * Math.Sin(t * 4.1)), 0, 1)))
                context.DrawImage(rim, new Rect(rim.Size), new Rect(centre.X - w / 2, centre.Y - h / 2, w, h));
        }

        // The hand: out of the rent and toward you, growing to more than fill the view, then closing.
        var reach = Math.Pow(Smooth(0.2, 0.8, p), 1.5);
        if (reach > 0 && Art(art, "recall-hand-open.png") is { } openHand)
        {
            var grasp = Smooth(0.63, 0.71, p);
            var height = dest.Height * PortalRy * 2 * (0.5 + 2.9 * reach);
            var width = height * openHand.Size.Width / openHand.Size.Height;
            var at = new Point(centre.X, centre.Y + dest.Height * 0.32 * reach);
            var sway = Matrix.CreateTranslation(-at.X, -at.Y) * Matrix.CreateRotation((3.5 * Math.Sin(t * 1.4) - 4 * grasp) * Math.PI / 180)
                       * Matrix.CreateTranslation(at.X, at.Y);
            var rect = new Rect(at.X - width / 2, at.Y - height / 2, width, height);
            var fadeIn = Math.Clamp(reach * 6, 0, 1) * 0.94;
            using (context.PushTransform(sway))
            {
                using (context.PushOpacity(fadeIn * (1 - grasp)))
                    context.DrawImage(openHand, new Rect(openHand.Size), rect);
                if (grasp > 0 && Art(art, "recall-hand-grasp.png") is { } closed)
                    using (context.PushOpacity(fadeIn * grasp))
                        context.DrawImage(closed, new Rect(closed.Size), rect);
            }
        }

        // It has you: a flash of the rent's light, into which the scene fades.
        var flash = Math.Exp(-Math.Pow((p - 0.83) / 0.05, 2));
        if (flash > 0.01)
            context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(235 * flash), 238, 226, 255)), new Rect(Bounds.Size));
    }

    private void DrawCaption(DrawingContext context, string caption, string? subtitle)
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
        var at = new Point((Bounds.Width - text.Width) / 2, Bounds.Height * (subtitle is null ? 0.89 : 0.85));
        var shadow = new FormattedText(caption, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Italic, FontWeight.SemiBold), size, Brushes.Black);
        context.DrawText(shadow, at + new Point(2, 2));
        context.DrawText(text, at);
        if (subtitle is null) return;
        var small = new FormattedText(subtitle, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Italic), size * 0.6, Parchment);
        context.DrawText(small, new Point((Bounds.Width - small.Width) / 2, at.Y + text.Height + 2));
    }

    /// <summary>"Press Space to continue", small, in the corner, while a scene holds.</summary>
    private void DrawPrompt(DrawingContext context)
    {
        var size = Math.Max(11, Bounds.Height / 48);
        var text = new FormattedText("Press Space to continue", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default), size, Parchment);
        context.DrawText(text, new Point(Bounds.Width - text.Width - size, Bounds.Height - text.Height - size * 0.6));
    }

    private static readonly IBrush Parchment = new SolidColorBrush(Color.FromRgb(0xE2, 0xD3, 0xAE));

    // --- The painted stairwell ------------------------------------------------------------------

    private const int Steps = 16;

    /// <summary>How far away a step is drawn (0 at the eye), given its place and how far the scene has gone.</summary>
    private static double Depth(int k, double p) => (k + 1 - (p * 2.4 % 1)) * 0.42;

    private void DrawStairwell(DrawingContext context, AmbientScene scene, double p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var cx = w / 2;
        var down = scene.Kind == SceneKind.StairsDown;
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


    // --- The painted scenes (when there is no picture) ---------------------------------------------

    private void Paint(DrawingContext context, AmbientScene scene, double p)
    {
        switch (scene.Kind)
        {
            case SceneKind.StairsDown or SceneKind.StairsUp: DrawStairwell(context, scene, p); break;
            case SceneKind.RecallUp or SceneKind.RecallDown: DrawRecall(context, scene.Kind == SceneKind.RecallUp, p); break;
            case SceneKind.DeepDescent or SceneKind.Trapdoor: DrawRecall(context, false, p); break;
            case SceneKind.QuestComplete: DrawRecall(context, true, p); break;
            case SceneKind.BossSlain: DrawUnique(context, scene, p); break;
            case SceneKind.Cavern or SceneKind.Moria or SceneKind.Lair: DrawCavern(context, p); break;
            case SceneKind.Labyrinth or SceneKind.Gauntlet: DrawLabyrinth(context, p); break;
            case SceneKind.Fortress: DrawFortress(context, p); break;
            case SceneKind.Danger: DrawDanger(context, p); break;
            case SceneKind.Unique: DrawUnique(context, scene, p); break;
            case SceneKind.Death: DrawTombstone(context, p); break;
        }
    }

    private static RadialGradientBrush Glow(Point centre, double radius, Color colour, double strength) => new()
    {
        Center = new RelativePoint(centre, RelativeUnit.Absolute),
        GradientOrigin = new RelativePoint(centre, RelativeUnit.Absolute),
        RadiusX = new RelativeScalar(radius, RelativeUnit.Absolute),
        RadiusY = new RelativeScalar(radius, RelativeUnit.Absolute),
        GradientStops =
        {
            new GradientStop(Color.FromArgb((byte)Math.Clamp(255 * strength, 0, 255), colour.R, colour.G, colour.B), 0),
            new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 1),
        },
    };

    /// <summary>A fixed scatter of points (the same every time), 0..1 in each axis.</summary>
    private static IEnumerable<(double X, double Y, double Z)> Scatter(int count, int seed)
    {
        var rng = new Random(seed);
        for (var i = 0; i < count; i++) yield return (rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
    }

    /// <summary>Recall: rings of light — widening and rising to the town, closing and sinking into the deep.</summary>
    private void DrawRecall(DrawingContext context, bool up, double p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var centre = new Point(w / 2, h * 0.45);
        var colour = up ? Color.FromRgb(255, 236, 190) : Color.FromRgb(150, 120, 255);
        context.FillRectangle(Glow(centre, Math.Max(w, h) * (up ? 0.25 + 0.4 * p : 0.65 - 0.4 * p), colour, up ? 0.55 : 0.4), new Rect(Bounds.Size));
        for (var i = 0; i < 7; i++)
        {
            var phase = (i / 7.0 + (up ? p : 1 - p) * 1.4) % 1;
            var r = Math.Max(w, h) * 0.6 * phase;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(200 * (1 - phase)), colour.R, colour.G, colour.B)), 2 + 6 * (1 - phase));
            context.DrawEllipse(null, pen, centre, r, r * 0.55);
        }
        foreach (var (x, y, z) in Scatter(90, up ? 11 : 12))
        {
            var drift = (y + (up ? -p : p) * (0.4 + z)) % 1;
            if (drift < 0) drift += 1;
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(120 + 120 * z), colour.R, colour.G, colour.B)), null,
                new Point(x * w, drift * h), 1 + 2 * z, 1 + 2 * z);
        }
    }

    /// <summary>A cavern: rough rock closing in around a pool of torchlight, water dripping.</summary>
    private void DrawCavern(DrawingContext context, double p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        context.FillRectangle(Glow(new Point(w / 2, h * 0.55), w * 0.45, Color.FromRgb(120, 90, 60), 0.9), new Rect(Bounds.Size));
        var rock = new SolidColorBrush(Color.FromRgb(24, 20, 17));
        foreach (var (x, y, z) in Scatter(26, 21))
        {
            // Boulders round the edge of the view.
            var angle = x * Math.PI * 2;
            var dist = 0.55 + 0.25 * z;
            var cx = w / 2 + Math.Cos(angle) * w * dist * 0.62;
            var cy = h * 0.5 + Math.Sin(angle) * h * dist * 0.75;
            var r = Math.Min(w, h) * (0.14 + 0.18 * y);
            var points = Enumerable.Range(0, 9).Select(k =>
            {
                var a = k / 9.0 * Math.PI * 2;
                var rr = r * (0.75 + 0.25 * Math.Sin(a * 3 + z * 7));
                return new Point(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr * 0.8);
            }).ToArray();
            Polygon(context, rock, points);
        }
        foreach (var (x, y, z) in Scatter(12, 22))
        {
            var fall = (y + p * (1.2 + z)) % 1;
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(180, 150, 190, 230)), null, new Point(w * (0.25 + 0.5 * x), h * (0.1 + 0.7 * fall)), 1.5, 3.5);
        }
    }

    /// <summary>A labyrinth: walls in perspective, turning off in every direction, fading into the dark.</summary>
    private void DrawLabyrinth(DrawingContext context, double p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var cx = w / 2;
        var vy = h * 0.48;
        var wall = Color.FromRgb(70, 64, 58);
        for (var k = 7; k >= 0; k--)
        {
            var d = k * 0.55 + (1 - p) * 0.3;
            var s = 1 / (1 + d);
            var half = w * 0.45 * s;
            var top = vy - h * 0.55 * s;
            var bottom = vy + h * 0.55 * s;
            var light = Math.Clamp(1.1 - d / 3.5, 0.05, 1);
            // A side wall on alternate sides, as the passages turn off.
            var side = (k % 3) switch { 0 => -1, 1 => 1, _ => 0 };
            var brush = new SolidColorBrush(Color.FromRgb((byte)(wall.R * light), (byte)(wall.G * light), (byte)(wall.B * light)));
            if (side != 0)
                context.FillRectangle(brush, new Rect(side < 0 ? cx - half : cx + half * 0.35, top, half * 0.65, bottom - top));
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(160 * light), 20, 18, 16)), 1.5),
                new Rect(cx - half, top, 2 * half, bottom - top));
        }
        context.FillRectangle(Glow(new Point(cx, vy), w * 0.2, Colors.Black, 1), new Rect(Bounds.Size));
    }

    /// <summary>A fortress: a great gate of dressed stone, banners hanging either side, torchlit.</summary>
    private void DrawFortress(DrawingContext context, double p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var stone = new SolidColorBrush(Color.FromRgb(48, 44, 42));
        context.FillRectangle(stone, new Rect(0, h * 0.08, w, h * 0.8));
        var mortar = new Pen(new SolidColorBrush(Color.FromRgb(28, 25, 24)), 1.5);
        var row = h * 0.06;
        for (var y = h * 0.08; y < h * 0.88; y += row)
        {
            context.DrawLine(mortar, new Point(0, y), new Point(w, y));
            var offset = ((int)(y / row) % 2) * row;
            for (var x = offset; x < w; x += row * 2) context.DrawLine(mortar, new Point(x, y), new Point(x, y + row));
        }
        // The gate: an arch of darkness.
        var gate = new Rect(w * 0.38, h * 0.3, w * 0.24, h * 0.58);
        var arch = new StreamGeometry();
        using (var g = arch.Open())
        {
            g.BeginFigure(new Point(gate.Left, gate.Bottom), true);
            g.LineTo(new Point(gate.Left, gate.Top + gate.Width / 2));
            g.ArcTo(new Point(gate.Right, gate.Top + gate.Width / 2), new Size(gate.Width / 2, gate.Width / 2), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(gate.Right, gate.Bottom));
            g.EndFigure(true);
        }
        context.DrawGeometry(Brushes.Black, new Pen(new SolidColorBrush(Color.FromRgb(90, 82, 74)), 4), arch);
        foreach (var side in new[] { -1, 1 })
        {
            var x = w / 2 + side * w * 0.27;
            var sway = Math.Sin(p * 6 + side) * w * 0.004;
            Polygon(context, new SolidColorBrush(Color.FromRgb(110, 20, 24)),
                new(x - w * 0.05, h * 0.12), new(x + w * 0.05, h * 0.12), new(x + w * 0.05 + sway, h * 0.5), new(x + sway, h * 0.45), new(x - w * 0.05 + sway, h * 0.5));
            var flicker = 0.85 + 0.15 * Math.Sin(p * 40 + side * 2);
            var torch = new Point(w / 2 + side * w * 0.16, h * 0.4);
            context.FillRectangle(Glow(torch, w * 0.18 * flicker, Color.FromRgb(255, 150, 60), 0.5), new Rect(Bounds.Size));
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 210, 120)), null, torch, h * 0.01 * flicker, h * 0.02 * flicker);
        }
    }

    /// <summary>A deadly feeling: blood-dark red closing in, pulsing like a heartbeat.</summary>
    private void DrawDanger(DrawingContext context, double p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var beat = 0.5 + 0.5 * Math.Pow(Math.Abs(Math.Sin(p * Math.PI * 3)), 6);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb((byte)(40 + 50 * beat), 0, 0)), new Rect(Bounds.Size));
        context.FillRectangle(Glow(new Point(w / 2, h / 2), Math.Max(w, h) * 0.55, Colors.Black, 1), new Rect(Bounds.Size));
        foreach (var (x, y, z) in Scatter(14, 31))
        {
            var drift = (x + p * 0.15 * (z - 0.5)) % 1;
            context.FillRectangle(Glow(new Point(drift * w, y * h), w * (0.1 + 0.15 * z), Color.FromRgb(0, 0, 0), 0.8), new Rect(Bounds.Size));
        }
    }

    /// <summary>A unique: its glyph, large, in its colour, looming out of the dark.</summary>
    private void DrawUnique(DrawingContext context, AmbientScene scene, double p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var colour = Color.FromUInt32(scene.GlyphColor);
        var centre = new Point(w / 2, h * 0.42);
        context.FillRectangle(Glow(centre, Math.Max(w, h) * 0.4, colour, 0.25 + 0.15 * p), new Rect(Bounds.Size));
        var size = h * (0.35 + 0.12 * p);
        var glyph = new FormattedText(scene.Glyph ?? "?", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("DejaVu Sans Mono, Consolas, Menlo, monospace"), FontStyle.Normal, FontWeight.Bold), size, new SolidColorBrush(colour));
        context.DrawText(glyph, new Point(centre.X - glyph.Width / 2, centre.Y - glyph.Height / 2));
    }

    /// <summary>Death: a tombstone under a night sky (Angband's own ends with one).</summary>
    private void DrawTombstone(DrawingContext context, double p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        foreach (var (x, y, z) in Scatter(80, 41))
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(80 + 150 * z * (0.7 + 0.3 * Math.Sin(p * 8 + x * 20))), 220, 225, 255)), null,
                new Point(x * w, y * h * 0.55), 0.8 + z, 0.8 + z);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(16, 20, 16)), new Rect(0, h * 0.72, w, h * 0.28));
        var stone = new Rect(w * 0.39, h * 0.22, w * 0.22, h * 0.52);
        var slab = new StreamGeometry();
        using (var g = slab.Open())
        {
            g.BeginFigure(new Point(stone.Left, stone.Bottom), true);
            g.LineTo(new Point(stone.Left, stone.Top + stone.Width / 2));
            g.ArcTo(new Point(stone.Right, stone.Top + stone.Width / 2), new Size(stone.Width / 2, stone.Width / 2), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(stone.Right, stone.Bottom));
            g.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(Color.FromRgb(120, 118, 112)), new Pen(new SolidColorBrush(Color.FromRgb(70, 68, 64)), 3), slab);
        var rip = new FormattedText("R.I.P.", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), h * 0.07, new SolidColorBrush(Color.FromRgb(50, 48, 45)));
        context.DrawText(rip, new Point(stone.Center.X - rip.Width / 2, stone.Top + stone.Height * 0.3));
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
