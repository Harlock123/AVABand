using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>Angband's '/': what a symbol stands for (ui-knowledge.c lookup_symbol).</summary>
public class IdentifySymbolTests
{
    private static readonly GameSession Game = GameSession.NewGame(TestData.Game, 1, "warrior");

    [Theory]
    [InlineData('o', "o - Orc.")]
    [InlineData('D', "D - Ancient Dragon/Wyrm.")]
    [InlineData('Z', "Z - Zephyr Hound.")]
    [InlineData('!', "! - Potion.")]
    [InlineData('`', "` - Unknown Symbol.")]
    public void ASymbol_IsNamed_ObjectsThenFeaturesThenMonsters(char symbol, string expected) =>
        Assert.Equal(expected, Game.IdentifySymbol(symbol));

    [Fact]
    public void EveryMonsterSymbol_HasAName()
    {
        foreach (var glyph in TestData.Game.Monsters.Select(m => m.Glyph).Distinct())
            Assert.DoesNotContain("Unknown Symbol", Game.IdentifySymbol(glyph));
    }
}
