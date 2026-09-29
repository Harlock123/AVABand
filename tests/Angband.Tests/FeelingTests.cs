using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>Level feelings (Angband 4.2 generate.c and cmd-cave.c).</summary>
public class FeelingTests
{
    private static GameSession Down(ulong seed = 31, params (string Id, bool Value)[] options)
    {
        var spec = CharacterSpec.Default("human", "warrior") with { Options = options.ToDictionary(o => o.Id, o => o.Value) };
        var game = GameSession.NewGame(TestData.Game, seed, spec);
        game.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Theory]
    [InlineData(7001, 1)]
    [InlineData(4501, 2)]
    [InlineData(801, 5)]
    [InlineData(151, 7)]
    [InlineData(50, 9)]
    public void MonsterFeelings_FollowAngbandsThresholds(long ratingPerLevel, int feeling) =>
        Assert.Equal(feeling, LevelFeelings.MonsterFeeling(ratingPerLevel * 10, 10));

    [Fact]
    public void ObjectFeelings_FollowAngbandsThresholds_AndArtifactsCount()
    {
        Assert.Equal(20, LevelFeelings.ObjectFeeling(160001 * 5, 5, false, false));
        Assert.Equal(100, LevelFeelings.ObjectFeeling(10 * 5, 5, false, false));
        Assert.Equal(60, LevelFeelings.ObjectFeeling(0, 5, artifact: true, loseArtifacts: false));
        Assert.Equal(10, LevelFeelings.ObjectFeeling(0, 5, artifact: true, loseArtifacts: true));
        Assert.Equal(0, LevelFeelings.ObjectFeeling(1_000_000, 0, true, true)); // the town
    }

    [Fact]
    public void Ratings_WeighLevelsAndValues()
    {
        var orc = TestData.Game.Monster("cave_orc")!;
        Assert.Equal((long)orc.Depth * orc.Depth, LevelFeelings.MonsterRating([orc], orc.Depth));
        Assert.Equal((long)orc.Depth * orc.Depth * 3, LevelFeelings.MonsterRating([orc], orc.Depth - 2)); // two levels out of depth

        var factory = new Angband.Core.Items.ObjectFactory(TestData.Game);
        var ring = factory.Create("ring_of_protection");
        var value = Angband.Core.Items.ItemValue.Of(ring, TestData.Game);
        var home = ring.Kind.MinDepth ?? ring.Kind.Level;
        Assert.Equal(value / 100 * (value / 100), LevelFeelings.ObjectRating([ring], TestData.Game, home));
        // Three levels out of depth: its value counts for 1 + 3/5 of itself (Angband make_object).
        var boosted = value + 3 * (value / 5);
        Assert.Equal(boosted / 100 * (boosted / 100), LevelFeelings.ObjectRating([ring], TestData.Game, home - 3));
    }

    [Fact]
    public void Books_the_class_cant_read_are_mostly_passed_over()
    {
        int Books(string cls, bool anyReader = false)
        {
            var game = GameSession.NewGame(TestData.Game, 5, cls);
            if (anyReader) game.Objects.CanBrowse = null;
            var rng = new Angband.Core.Randomness.GameRandom(11);
            var books = 0;
            for (var i = 0; i < 4000; i++)
                if (game.Objects.Make(rng, 10) is { } o && game.Objects.IsBook(o.Kind)) books++;
            return books;
        }
        var all = Books("warrior", anyReader: true);
        var warrior = Books("warrior"); // reads no book: most are rerolled (a fifth kept, three tries)
        Assert.True(warrior * 3 < all, $"warrior {warrior} of {all}");
        Assert.True(Books("mage") > warrior); // the mage keeps the magic books
    }

    [Fact]
    public void Describe_JoinsDangerAndTreasure_WhenBothAreKnown()
    {
        var level = new Level(TestData.Game.Terrain, 10, 10, depth: 5) { Feeling = 20 + 9 };
        Assert.Equal("This seems a quiet, peaceful place.", LevelFeelings.Describe(level));
        Assert.Equal("LF:1-?", LevelFeelings.Status(level));
        level.FeelingSquaresSeen = LevelFeelings.FeelingNeed;
        Assert.Equal("This seems a quiet, peaceful place, yet there are superb treasures here.", LevelFeelings.Describe(level));
        Assert.Equal("LF:1-9", LevelFeelings.Status(level));
        level.Feeling = 100 + 1;
        Assert.Equal("Omens of death haunt this place, yet there is naught but cobwebs here.", LevelFeelings.Describe(level));
        Assert.Equal("Looks like a typical town.", LevelFeelings.Describe(new Level(TestData.Game.Terrain, 10, 10, depth: 0)));
    }

    [Fact]
    public void ArrivingInTheDungeon_GivesTheDangerFeeling_ThenTheTreasureAfterExploring()
    {
        var game = Down();
        var messages = Messages(game);
        game.Execute(new TakeStairsCommand(Down: true));
        Assert.Contains(messages, m => LevelFeelings.MonsterTexts.Any(t => m.StartsWith(t)));
        Assert.InRange(game.Level.Feeling % 10, 1, 9);
        Assert.InRange(game.Level.Feeling / 10, 1, 10);
        Assert.InRange(game.Level.FeelSquares.Count + game.Level.FeelingSquaresSeen, 90, LevelFeelings.FeelingTotal);
        Assert.All(game.Level.FeelSquares, p => Assert.True(game.Level.IsPassable(p)));

        // Seeing the tenth feeling square brings the treasure feeling (once).
        game.Level.FeelingSquaresSeen = 0;
        game.Level.FeelSquares.Clear();
        foreach (var p in game.Level.AllLocs().Where(p => game.Level[p].Has(SquareFlags.Seen)).Take(LevelFeelings.FeelingNeed))
            game.Level.FeelSquares.Add(p);
        Assert.Equal(LevelFeelings.FeelingNeed, game.Level.FeelSquares.Count);
        messages.Clear();
        game.UpdateView();
        Assert.Single(messages, m => m.StartsWith("You feel that "));
        Assert.Matches(@"^LF:\d-[\d$*]$", game.FeelingStatus!);
    }

    [Fact]
    public void Feelings_CanBeTurnedOffAtBirth()
    {
        var game = Down(32, (OptionIds.Feelings, false));
        var messages = Messages(game);
        game.Execute(new TakeStairsCommand(Down: true));
        Assert.DoesNotContain(messages, m => LevelFeelings.MonsterTexts.Any(t => m.StartsWith(t)));
        Assert.False(game.ShowFeeling());
        Assert.Null(game.FeelingStatus);
    }

    [Fact]
    public void CtrlF_RepeatsTheFeeling_WithoutTakingTime()
    {
        var game = Down(33);
        game.Execute(new TakeStairsCommand(Down: true));
        var messages = Messages(game);
        var turn = game.GameTurn;
        Assert.False(game.Execute(new FeelingCommand()));
        Assert.Equal(turn, game.GameTurn);
        Assert.Single(messages);
    }

    [Fact]
    public void Feelings_AreSaved()
    {
        var game = Down(34);
        game.Execute(new TakeStairsCommand(Down: true));
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(game.Level.Feeling, loaded.Level.Feeling);
        Assert.Equal(game.Level.FeelingSquaresSeen, loaded.Level.FeelingSquaresSeen);
        Assert.Equal(game.Level.FeelSquares.OrderBy(p => p.Y).ThenBy(p => p.X), loaded.Level.FeelSquares.OrderBy(p => p.Y).ThenBy(p => p.X));
    }
}
