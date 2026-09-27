using Angband.Core.Randomness;

namespace Angband.Tests;

public class DiceTests
{
    [Theory]
    [InlineData("2d6", 2, 6, 0)]
    [InlineData("d8", 1, 8, 0)]
    [InlineData("3d4+2", 3, 4, 2)]
    [InlineData("1d10-1", 1, 10, -1)]
    [InlineData("5", 0, 0, 5)]
    public void Parse_ReadsExpressions(string text, int count, int sides, int bonus)
    {
        Assert.Equal(new Dice(count, sides, bonus), Dice.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2x6")]
    [InlineData("d")]
    [InlineData("+3")]
    public void Parse_RejectsGarbage(string text) => Assert.False(Dice.TryParse(text, out _));

    [Fact]
    public void Roll_RespectsMinAndMax()
    {
        var dice = Dice.Parse("2d6+3");
        var rng = new GameRandom(1);
        for (var i = 0; i < 2000; i++) Assert.InRange(dice.Roll(rng), dice.Min, dice.Max);
        Assert.Equal(5, dice.Min);
        Assert.Equal(15, dice.Max);
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        foreach (var text in new[] { "2d6", "3d4+2", "1d10-1", "7" })
            Assert.Equal(text, Dice.Parse(text).ToString());
    }
}
