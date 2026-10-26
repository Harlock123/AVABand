using Angband.Core.Records;
using Avalonia;
using Avalonia.Media;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// How a card frame looks (AVABand's own, an unlock earned through feats): a band of colour round the
/// card, a thin line inside it, ornaments at the corners (and, for some, half-way along the sides) and
/// sometimes a glow — all drawn, no pictures needed.
/// </summary>
/// <param name="Id">The unlock's key (<see cref="UnlockDef.Key"/>), as kept in the settings.</param>
public sealed record CardFrameStyle(string Id, string Name, IBrush Outer, IBrush Inner, IBrush Ornament, double Thickness,
    Geometry OrnamentShape, bool SideOrnaments, BoxShadows Glow, double OrnamentScale = 1.4)
{
    /// <summary>The thin inner line sits just inside the band.</summary>
    public Thickness InnerMargin => new(Thickness + 6);

    public Thickness BandThickness => new(Thickness);

    /// <summary>Rivets sit on the band; larger ornaments sit across its inner edge.</summary>
    private bool OnBand => OrnamentScale < 1;

    /// <summary>The ornaments' size: a rivet smaller than the band, others a little wider.</summary>
    public double OrnamentSize => Thickness * OrnamentScale + (OnBand ? 0 : 6);

    /// <summary>Where each ornament sits: centred on the band, or on its inner edge (so a corner's is whole, not cut by the card's edge).</summary>
    public Thickness OrnamentMargin => new(OnBand ? (Thickness - OrnamentSize) / 2 : Math.Max(1, Thickness - OrnamentSize / 2));
}

/// <summary>Every card frame, by the unlock that earns it.</summary>
public static class CardFrames
{
    private static readonly Geometry Diamond = Geometry.Parse("M10,0 L20,10 L10,20 L0,10 Z");
    private static readonly Geometry Rivet = Geometry.Parse("M0,10 A10,10 0 1 1 20,10 A10,10 0 1 1 0,10 Z");
    private static readonly Geometry Leaf = Geometry.Parse("M0,20 C0,7 7,0 20,0 C20,13 13,20 0,20 Z");

    private static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

    /// <summary>A metal's sheen: dark, bright, dark again, across the card.</summary>
    private static IBrush Sheen(params string[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
        for (var i = 0; i < colors.Length; i++) brush.GradientStops.Add(new GradientStop(Color.Parse(colors[i]), (double)i / (colors.Length - 1)));
        return brush;
    }

    public static IReadOnlyList<CardFrameStyle> All { get; } =
    [
        new("trophy", "Trophy frame", Sheen("#6b4423", "#c99252", "#7a4e28", "#c99252", "#6b4423"), Solid("#e8c48a"), Solid("#f0d49a"),
            14, Rivet, true, default, 0.6),
        new("spectral", "Spectral frame", Solid("#9970d8d0"), Solid("#c8f4ff"), Solid("#e0ffff"),
            6, Diamond, false, BoxShadows.Parse("inset 0 0 70 14 #8070e0d8")),
        new("iron", "Iron frame", Sheen("#2e3136", "#5f646b", "#34383d", "#5f646b", "#2e3136"), Solid("#8d939b"), Solid("#a9afb7"),
            22, Rivet, true, default, 0.55),
        new("dawn", "Dawn frame", Sheen("#f4b35e", "#e86f8a", "#8a6fd0"), Solid("#ffe3a8"), Solid("#fff2c8"),
            12, Diamond, false, BoxShadows.Parse("inset 0 0 40 4 #50f4b35e")),
        new("gold", "Gold frame", Sheen("#8a6a1a", "#f2d464", "#a8822a", "#f2d464", "#8a6a1a"), Solid("#fff0b0"), Solid("#fff6cc"),
            16, Diamond, true, BoxShadows.Parse("inset 0 0 36 4 #60f0d060")),
        new("silver", "Silver frame", Sheen("#6c747c", "#e2e8ee", "#868e96", "#e2e8ee", "#6c747c"), Solid("#f4f8fc"), Solid("#ffffff"),
            12, Diamond, false, default),
        new("elven", "Elven frame", Sheen("#24573a", "#6fae7a", "#2f6b45", "#6fae7a", "#24573a"), Solid("#dce6ec"), Solid("#e8f0f4"),
            12, Leaf, true, default),
        new("mithril", "Mithril frame", Sheen("#9ab0c8", "#f4f8ff", "#b8c8d8", "#f4f8ff", "#9ab0c8"), Solid("#ffffff"), Solid("#ffffff"),
            14, Diamond, true, BoxShadows.Parse("inset 0 0 50 6 #70c8e0ff")),
    ];

    public static CardFrameStyle? Find(string? id) => id is null ? null : All.FirstOrDefault(f => f.Id == id);
}
