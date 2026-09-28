using Angband.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Angband.Avalonia.Controls;

/// <summary>
/// Depth over time: game turns across, depth down (the town at the top), as a stepped line —
/// each level held until the next; uniques killed are red dots on it, artifacts found violet.
/// </summary>
public sealed class JourneyGraph : Control
{
    public static readonly StyledProperty<JourneyViewModel?> JourneyProperty =
        AvaloniaProperty.Register<JourneyGraph, JourneyViewModel?>(nameof(Journey));

    public JourneyViewModel? Journey
    {
        get => GetValue(JourneyProperty);
        set => SetValue(JourneyProperty, value);
    }

    static JourneyGraph() => AffectsRender<JourneyGraph>(JourneyProperty);

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14));
    private static readonly IPen Grid = new Pen(new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x32)), 1);
    private static readonly IPen Line = new Pen(new SolidColorBrush(Color.FromRgb(0xE2, 0xD3, 0xAE)), 2);
    private static readonly IBrush Label = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99));
    private static readonly IBrush UniqueDot = new SolidColorBrush(Color.FromRgb(0xE0, 0x40, 0x40));
    private static readonly IBrush ArtifactDot = new SolidColorBrush(Color.FromRgb(0xB0, 0x70, 0xFF));

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        if (Journey is not { } j || j.Points.Count == 0) return;
        const double left = 64, right = 16, top = 14, bottom = 26;
        var plot = new Rect(left, top, Math.Max(10, bounds.Width - left - right), Math.Max(10, bounds.Height - top - bottom));
        double X(long turn) => plot.Left + plot.Width * turn / j.MaxTurn;
        double Y(int depth) => plot.Top + plot.Height * depth / j.MaxDepth;

        // Depth lines every so often, labelled in feet.
        var step = Math.Max(1, (int)Math.Ceiling(j.MaxDepth / 6.0));
        for (var d = 0; d <= j.MaxDepth; d += step)
        {
            context.DrawLine(Grid, new Point(plot.Left, Y(d)), new Point(plot.Right, Y(d)));
            Text(context, d == 0 ? "Town" : $"{d * 50} ft", new Point(4, Y(d) - 8));
        }
        Text(context, "0", new Point(plot.Left, plot.Bottom + 6));
        Text(context, $"{j.MaxTurn / 10:N0} turns", new Point(plot.Right - 90, plot.Bottom + 6));

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(X(j.Points[0].Turn), Y(j.Points[0].Depth)), false);
            for (var i = 1; i < j.Points.Count; i++)
            {
                g.LineTo(new Point(X(j.Points[i].Turn), Y(j.Points[i - 1].Depth))); // held until now
                g.LineTo(new Point(X(j.Points[i].Turn), Y(j.Points[i].Depth)));
            }
            g.EndFigure(false);
        }
        context.DrawGeometry(null, Line, geometry);
        foreach (var m in j.Markers)
            context.DrawEllipse(m.Artifact ? ArtifactDot : UniqueDot, null, new Point(X(m.Turn), Y(m.Depth)), 5, 5);
    }

    private static void Text(DrawingContext context, string text, Point at) =>
        context.DrawText(new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 11, Label), at);
}
