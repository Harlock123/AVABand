using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>Books name their kind (as Angband's do) and say whether they're the player's.</summary>
public class BookNamingTests
{
    [Theory]
    [InlineData("first_spells", "a Magic Book of [First Spells]")]
    [InlineData("novices_handbook", "a Holy Book of [Novice's Handbook]")]
    [InlineData("lesser_charms", "a Nature Book of [Lesser Charms]")]
    [InlineData("into_the_shadows", "a Necromantic Tome of [Into the Shadows]")]
    public void A_book_says_what_kind_it_is(string kind, string name)
    {
        var game = GameSession.NewGame(TestData.Game, 1, "warrior");
        Assert.Equal(name, game.Describe(game.Objects.Create(kind)));
    }

    [Fact]
    public void Several_are_plural_on_the_kind()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "warrior");
        Assert.Equal("2 Holy Books of [Novice's Handbook]", game.Describe(game.Objects.Create("novices_handbook", 2)));
    }

    [Theory]
    [InlineData("priest", "novices_handbook", "for you")]
    [InlineData("paladin", "novices_handbook", "for you")]
    [InlineData("priest", "first_spells", "not for a Priest")]
    [InlineData("mage", "novices_handbook", "not for a Mage")]
    [InlineData("warrior", "first_spells", "not for a Warrior")]
    [InlineData("necromancer", "into_the_shadows", "for you")]
    public void A_book_says_whether_it_is_yours(string cls, string kind, string note)
    {
        var game = GameSession.NewGame(TestData.Game, 1, cls);
        Assert.Equal(note, game.BookNote(game.Objects.Create(kind)));
    }

    [Fact]
    public void Anything_else_has_no_note()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "priest");
        Assert.Null(game.BookNote(game.Objects.Create("ration_of_food")));
    }
}
