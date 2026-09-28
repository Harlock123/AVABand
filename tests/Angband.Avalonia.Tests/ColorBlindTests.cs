using Angband.Avalonia.ViewModels;
using Angband.Data;

namespace Angband.Avalonia.Tests;

/// <summary>
/// The colour-blind friendly palette: under simulated deuteranopia and protanopia (Machado, Oliveira
/// and Fernandes 2009, full severity) the health bar's colours stay far apart, much further than
/// Angband's own.
/// </summary>
public class ColorBlindTests
{
    private static readonly double[,] Deuteranopia =
        { { 0.367322, 0.860646, -0.227968 }, { 0.280085, 0.672501, 0.047413 }, { -0.011820, 0.042940, 0.968881 } };
    private static readonly double[,] Protanopia =
        { { 0.152286, 1.052583, -0.204868 }, { 0.114503, 0.786281, 0.099216 }, { -0.003882, -0.048116, 1.051998 } };

    private static readonly string[] HealthColours = ["LightGreen", "Yellow", "Orange", "LightRed", "Red"];

    private static double[] Simulate(uint argb, double[,] m)
    {
        static double Lin(double c) => (c /= 255) <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        static double Unlin(double c) => 255 * ((c = Math.Clamp(c, 0, 1)) <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055);
        double[] rgb = [Lin((argb >> 16) & 0xFF), Lin((argb >> 8) & 0xFF), Lin(argb & 0xFF)];
        return [.. Enumerable.Range(0, 3).Select(k => Unlin(m[k, 0] * rgb[0] + m[k, 1] * rgb[1] + m[k, 2] * rgb[2]))];
    }

    private static double ClosestPair(MapCellBuilder cells, double[,] m)
    {
        var sim = HealthColours.Select(c => Simulate(cells.Color(c), m)).ToList();
        var closest = double.MaxValue;
        for (var i = 0; i < sim.Count; i++)
        for (var j = i + 1; j < sim.Count; j++)
            closest = Math.Min(closest, Math.Sqrt(sim[i].Zip(sim[j], (a, b) => (a - b) * (a - b)).Sum()));
        return closest;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheHealthColours_StayApart_ForRedGreenColourBlindness(bool deuteranopia)
    {
        var m = deuteranopia ? Deuteranopia : Protanopia;
        var cells = new MapCellBuilder(DataLoader.Load(DataLoader.DefaultDataDirectory));
        var angband = ClosestPair(cells, m);
        cells.ColorBlind = true;
        var friendly = ClosestPair(cells, m);
        Assert.True(friendly > 55, $"closest pair only {friendly:0.0} apart");
        Assert.True(friendly > angband + 15, $"{friendly:0.0} is no better than {angband:0.0}");
    }

    [Fact]
    public void ColoursOutsideTheRedGreenRange_AreLeftAlone()
    {
        var cells = new MapCellBuilder(DataLoader.Load(DataLoader.DefaultDataDirectory));
        var white = cells.Color("White");
        var umber = cells.Color("Umber");
        cells.ColorBlind = true;
        Assert.Equal(white, cells.Color("White"));
        Assert.Equal(umber, cells.Color("Umber"));
        Assert.NotEqual(0xFFC00000u, cells.Color("Red"));
    }
}
