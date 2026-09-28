using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2 cmd-obj.c do_cmd_study: classes with CHOOSE_SPELLS pick the spell they learn;
/// priests and paladins pick a book and are granted one of its learnable spells at random.
/// </summary>
public class StudyTests
{
    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Theory]
    [InlineData("mage", true)]
    [InlineData("druid", true)]
    [InlineData("necromancer", true)]
    [InlineData("rogue", true)]
    [InlineData("ranger", true)]
    [InlineData("blackguard", true)]
    [InlineData("priest", false)]
    [InlineData("paladin", false)]
    public void SixClassesChoose_PriestsAndPaladinsDoNot(string cls, bool chooses) =>
        Assert.Equal(chooses, GameSession.NewGame(TestData.Game, 1, cls).ChoosesSpells);

    [Fact]
    public void APriest_IsGrantedARandomSpellOfTheBook()
    {
        var learned = new HashSet<string>();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var game = GameSession.NewGame(TestData.Game, seed, "priest");
            var options = game.StudyableSpells().Select(s => s.Id).ToList();
            Assert.True(game.Execute(new StudyCommand(Book: "novices_handbook")));
            var spell = Assert.Single(game.Player.LearnedSpells);
            Assert.Contains(spell, options);
            learned.Add(spell);
        }
        Assert.True(learned.Count > 1, "always the same prayer"); // four level-1 prayers to be given
    }

    [Fact]
    public void APriest_WithNothingToLearnInTheBook_IsToldSo()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "priest");
        game.Player.Inventory.Add(game.Objects.Create("cleansing_power")); // level 7 prayers and up
        var said = Messages(game);
        Assert.False(game.Execute(new StudyCommand(Book: "cleansing_power")));
        Assert.Contains("You cannot learn any prayers in that book.", said);
        Assert.Empty(game.Player.LearnedSpells);
    }

    [Fact]
    public void AMage_StillLearnsTheSpellItChooses()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "mage");
        Assert.True(game.Execute(new StudyCommand("find_traps_doors")));
        Assert.Equal(["find_traps_doors"], game.Player.LearnedSpells);
    }

    [Fact]
    public void StudyableBooks_AreTheBooksWithSomethingToLearn()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "priest");
        game.Player.Inventory.Add(game.Objects.Create("cleansing_power"));
        Assert.Equal(["novices_handbook"], game.StudyableBooks());
    }
}
