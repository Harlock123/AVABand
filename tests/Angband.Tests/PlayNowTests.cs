using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>Play now: its race and class pairs, and the stats it buys for them.</summary>
public class PlayNowTests
{
    private static Dictionary<string, int> Twelves() => CharacterSpec.StatIds.ToDictionary(s => s, _ => 12);

    [Fact]
    public void Every_pair_is_a_real_race_and_class_with_its_stats_named()
    {
        Assert.InRange(PlayNow.Combos.Count, 6, 10);
        Assert.Equal(PlayNow.Combos.Count, PlayNow.Combos.Select(c => (c.RaceId, c.ClassId)).Distinct().Count());
        foreach (var combo in PlayNow.Combos)
        {
            Assert.NotNull(TestData.Game.Race(combo.RaceId));
            Assert.NotNull(TestData.Game.Class(combo.ClassId));
            Assert.All(combo.Stats, s => Assert.Contains(s, CharacterSpec.StatIds));
            Assert.False(string.IsNullOrWhiteSpace(combo.Blurb));
        }
    }

    [Fact]
    public void Points_go_on_the_main_stat_first_then_round_the_rest_within_the_budget()
    {
        var budget = TestData.Game.Constants.BirthPoints;
        var stats = PlayNow.SpendPoints(Twelves(), ["str", "con", "dex"], budget);
        Assert.Null(Birth.ValidatePointBuy(stats, budget));
        Assert.Equal(16, stats["str"]);
        Assert.True(stats["con"] > 12 && stats["dex"] > 12);
        Assert.Equal(12, stats["int"]);
        Assert.Equal(12, stats["wis"]);
        Assert.InRange(budget - Birth.PointsSpent(stats), 0, 1); // (as near the whole budget as the costs allow)
    }

    [Fact]
    public void Every_pair_starts_in_the_town_with_the_classic_options()
    {
        var budget = TestData.Game.Constants.BirthPoints;
        foreach (var combo in PlayNow.Combos)
        {
            var classic = Challenges.Presets.Single(p => p.Id == "classic").Options;
            var spec = new CharacterSpec("Tester", combo.RaceId, combo.ClassId, PlayNow.SpendPoints(Twelves(), combo.Stats, budget),
                StatMethod.PointBuy, new Dictionary<string, bool>(classic));
            var game = GameSession.NewGame(TestData.Game, 7, spec);
            Assert.Equal(0, game.Player.Depth);
            Assert.Equal(combo.ClassId, game.Player.Class?.Id);
            Assert.Equal(combo.RaceId, game.Player.Race?.Id);
            Assert.Empty(Challenges.Names(id => game.Options[id]));
        }
    }

    [Theory]
    [InlineData("Elf Ranger", "an")]
    [InlineData("Hobbit Rogue", "a")]
    [InlineData("High-Elf Mage", "a")]
    public void The_article_fits_the_word(string word, string article) => Assert.Equal(article, PlayNow.Article(word));
}
