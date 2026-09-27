using Angband.Core.Definitions;
using Angband.Core.Generation;

namespace Angband.Tests;

public class TownGenerationTests
{
    private static readonly DungeonGenerator Generator = new(TestData.Game);

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(31337UL)]
    [InlineData(ulong.MaxValue)]
    public void Town_IsValidWithEveryShopReachable(ulong seed)
    {
        var result = Generator.Generate(new LevelRequest(0, seed));
        var level = result.Level;
        DungeonGenerationTests.AssertValid(result);

        var shops = level.FindFeature(TerrainFlags.Shop).ToList();
        Assert.Equal(TestData.Game.Town.Shops.Count, shops.Count);
        Assert.Equal(TestData.Game.Town.Shops.Order(), shops.Select(p => level.FeatureAt(p).Id).Order());

        Assert.Single(level.FindFeature(TerrainFlags.DownStair));
        Assert.Empty(level.FindFeature(TerrainFlags.UpStair));
        Assert.True(level.IsLit);
    }

    [Fact]
    public void Town_LayoutDependsOnlyOnSeed()
    {
        var a = Generator.Generate(new LevelRequest(0, 555)).Level.ToAscii();
        var b = Generator.Generate(new LevelRequest(0, 555, StairArrival.Ascended)).Level.ToAscii();
        Assert.Equal(a, b);
    }

    [Fact]
    public void ReturningFromDungeon_StartsOnTheDownStaircase()
    {
        var result = Generator.Generate(new LevelRequest(0, 555, StairArrival.Ascended));
        Assert.True(result.Level.Has(result.PlayerStart, TerrainFlags.DownStair));
    }
}
