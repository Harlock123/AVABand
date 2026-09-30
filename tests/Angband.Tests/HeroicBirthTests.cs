using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Core.Randomness;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>AVABand's superlative characters: the heroic roll, heroic point-buy and the autoroller.</summary>
public class HeroicBirthTests
{
    [Fact]
    public void TheHeroicRoll_IsFarBetterThanTheOrdinaryOne()
    {
        var rng = new GameRandom(7);
        var heroic = Enumerable.Range(0, 2000).SelectMany(_ => HeroicBirth.RollStats(rng).Values).ToList();
        var plain = Enumerable.Range(0, 2000).SelectMany(_ => Birth.RollStats(rng).Values).ToList();
        Assert.All(heroic, v => Assert.InRange(v, 14, HeroicBirth.HeroicMax));
        Assert.Contains(HeroicBirth.HeroicMax, heroic);               // 18/50 turns up
        Assert.Contains(14, heroic);
        Assert.InRange(heroic.Average(), 18, 19);                    // about 18/10
        Assert.True(heroic.Average() > plain.Average() + 5);
    }

    [Fact]
    public void HeroicPointBuy_Reaches18Slash50_WithABiggerBudget()
    {
        Assert.Equal(Birth.Cost(18), HeroicBirth.Cost(18));          // Angband's costs up to 18
        Assert.Equal(12 + 5 * 3, HeroicBirth.Cost(23));              // then 3 a step to 18/50
        var constants = TestData.Game.Constants;
        Assert.Equal(50, HeroicBirth.Budget(StatMethod.HeroicPointBuy, constants));
        Assert.Equal(constants.BirthPoints, HeroicBirth.Budget(StatMethod.PointBuy, constants));

        var stats = new Dictionary<string, int> { ["str"] = 23, ["int"] = 10, ["wis"] = 10, ["dex"] = 18, ["con"] = 16 };
        Assert.Null(HeroicBirth.ValidatePointBuy(StatMethod.HeroicPointBuy, stats, constants)); // 27 + 12 + 6 = 45
        Assert.NotNull(HeroicBirth.ValidatePointBuy(StatMethod.PointBuy, stats, constants));  // not in a normal one
        stats["int"] = 23;
        Assert.Contains("only have 50", HeroicBirth.ValidatePointBuy(StatMethod.HeroicPointBuy, stats, constants));
        stats["int"] = 24;
        Assert.Contains("18/50", HeroicBirth.ValidatePointBuy(StatMethod.HeroicPointBuy, stats, constants));

        // Unspent heroic points become gold, as ordinary ones do.
        stats["int"] = 10;
        var spec = new CharacterSpec("Hero", "human", "warrior", stats, StatMethod.HeroicPointBuy);
        Assert.Equal(constants.StartGold + 5 * 50, Birth.StartingGold(spec, constants));
    }

    [Fact]
    public void TheAutoroller_MeetsEveryMinimum_OrSaysWhyNot()
    {
        var race = TestData.Game.Race("dwarf");
        var cls = TestData.Game.Class("warrior");
        var minimums = new Dictionary<string, int> { ["str"] = 21, ["con"] = 20 };  // 18/30 and 18/20, after race and class
        var found = HeroicBirth.AutoRoll(StatMethod.Roll, minimums, race, cls, new GameRandom(3));
        Assert.NotNull(found);
        Assert.True(Birth.FinalStat(found.Value.Stats["str"], race, cls, "str") >= 21);
        Assert.True(Birth.FinalStat(found.Value.Stats["con"], race, cls, "con") >= 20);
        Assert.True(found.Value.Rolls >= 1);

        // Out of reach: a rolled stat is at most 17, so the best is 17 plus race and class.
        var impossible = new Dictionary<string, int> { ["int"] = 40 };
        Assert.Contains("INT can't reach 18/220", HeroicBirth.Unreachable(StatMethod.Roll, impossible, race, cls));
        Assert.Null(HeroicBirth.Unreachable(StatMethod.HeroicRoll, minimums, race, cls));
        Assert.Null(HeroicBirth.AutoRoll(StatMethod.Roll, impossible, race, cls, new GameRandom(3), maxRolls: 500));
    }

    [Fact]
    public void AHeroicCharacter_IsScored_ButTagged_AndKeepsTheTag()
    {
        var stats = HeroicBirth.RollStats(new GameRandom(11));
        var game = GameSession.NewGame(TestData.Game, 5, new CharacterSpec("Hero", "human", "warrior", stats, StatMethod.HeroicRoll));
        Assert.True(game.Player.HeroicBirth);
        Assert.False(game.IsCheater);
        Assert.True(ScoreEntry.For(game).Heroic);
        Assert.Contains("Heroic: born with AVABand's heroic stats (the score counts 75%)", CharacterDump.Build(game));
        Assert.Equal(stats["str"], game.Player.BaseStats["str"]);

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        Assert.True(SaveGame.Load(TestData.Game, stream).Player.HeroicBirth);

        var plain = GameSession.NewGame(TestData.Game, 5, CharacterSpec.Default("human", "warrior"));
        Assert.False(plain.Player.HeroicBirth);
        Assert.False(ScoreEntry.For(plain).Heroic);
        Assert.DoesNotContain("Heroic", CharacterDump.Build(plain));
    }

    [Fact]
    public void AHeroicScore_CountsThreeQuarters()
    {
        var plain = GameSession.NewGame(TestData.Game, 5, CharacterSpec.Default("human", "warrior"));
        var heroic = GameSession.NewGame(TestData.Game, 5,
            new CharacterSpec("Hero", "human", "warrior", HeroicBirth.RollStats(new GameRandom(1)), StatMethod.HeroicRoll));
        foreach (var g in new[] { plain, heroic })
        {
            g.Player.Experience = g.Player.MaxExperience = 10_000;
            g.Player.MaxDepth = 20;
        }
        Assert.Equal(12_000, Scoring.Points(plain.Player));
        Assert.Equal(9_000, Scoring.Points(heroic.Player));
        Assert.Equal(9_000, ScoreEntry.For(heroic).Points);
        Assert.Contains("Score 9000 points", CharacterDump.Build(heroic));
    }
}
