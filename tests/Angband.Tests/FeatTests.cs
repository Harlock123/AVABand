using Angband.Core.Game;
using Angband.Core.Quests;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>Feats: milestones across characters, kept once done, and never for a cheat.</summary>
public class FeatTests
{
    private static readonly IReadOnlyList<FeatDef> All = Feats.All(TestData.Game);

    private static GameSession Game(ulong seed = 5) => GameSession.NewGame(TestData.Game, seed, "warrior");

    [Fact]
    public void TheFeats_AreEachNamedOnce_AndCoverEveryClassesWin()
    {
        Assert.Equal(All.Count, All.Select(f => f.Id).Distinct().Count());
        Assert.All(TestData.Game.Classes, c => Assert.Contains(All, f => f.Id == $"win_{c.Id}"));
        Assert.Contains(All, f => f.Name == "Sauron, the Sorcerer Falls" || f.Id == "quest_sauron");
        Assert.Contains(All, f => f.Id == "quest_morgoth");
    }

    [Fact]
    public void Reaching_A_Depth_IsAFeat_Kept_OnceOnly()
    {
        var game = Game();
        var book = new FeatBook();
        Assert.Empty(book.Check(game, All));
        game.Player.MaxDepth = 20;
        var fresh = book.Check(game, All);
        Assert.Equal(["depth_10", "depth_20"], fresh.Select(f => f.Id));
        Assert.Equal("Adventurer the Human Warrior", book.Earned["depth_20"].Character);
        Assert.Empty(book.Check(game, All));                  // done: not again

        // Another character doesn't take it from the first.
        var second = Game(6);
        second.Player.Name = "Second";
        second.Player.MaxDepth = 40;
        Assert.Equal(["depth_40"], book.Check(second, All).Select(f => f.Id));
        Assert.Equal("Adventurer the Human Warrior", book.Earned["depth_10"].Character);
    }

    [Fact]
    public void A_Cheat_Earns_None()
    {
        var game = Game();
        game.MarkDebugUsed();
        game.Player.MaxDepth = 100;
        game.Player.IsWinner = true;
        Assert.Empty(new FeatBook().Check(game, All));
    }

    [Fact]
    public void Winning_Counts_ForTheClass()
    {
        var game = Game();
        game.Player.IsWinner = true;
        var ids = new FeatBook().Check(game, All).Select(f => f.Id).ToList();
        Assert.Contains("win", ids);
        Assert.Contains("win_warrior", ids);
        Assert.DoesNotContain("win_mage", ids);
    }

    [Fact]
    public void Every_Story_Quest_Done_IsAFeat_But_A_Lost_One_DoesntCount()
    {
        var game = Game();
        foreach (var q in TestData.Game.AvaQuests) game.AvaQuests.Quests[q.Id] = new AvaQuestState { Id = q.Id, Stage = "done" };
        game.AvaQuests.Quests["apprentice"].Stage = "lost";
        var ids = new FeatBook().Check(game, All).Select(f => f.Id).ToList();
        Assert.Contains("ava_quest", ids);
        Assert.DoesNotContain("ava_quests_all", ids);
        game.AvaQuests.Quests["apprentice"].Stage = "done";
        Assert.Contains("ava_quests_all", new FeatBook().Check(game, All).Select(f => f.Id));
    }

    [Fact]
    public void TheBook_IsSaved_AndRead_Back()
    {
        var path = Path.Combine(Path.GetTempPath(), $"avaband-feats-{Guid.NewGuid():N}.json");
        try
        {
            var game = Game();
            game.Player.MaxDepth = 10;
            var book = new FeatBook();
            book.Check(game, All, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
            book.Save(path);
            var back = FeatBook.Load(path);
            Assert.Equal(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), back.Earned["depth_10"].WhenUtc);
            File.WriteAllText(path, "not json");
            Assert.Empty(FeatBook.Load(path).Earned);          // damaged: starts afresh
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Feats_DoneBeforeThereWereFeats_AreFilledIn_FromTheHighScores()
    {
        var book = new FeatBook();
        var entries = new[]
        {
            new ScoreEntry { Name = "Later", Race = "Dwarf", Class = "Priest", MaxDepth = 45, Level = 28, MaxLevel = 30, DateUtc = new DateTime(2026, 9, 20) },
            new ScoreEntry { Name = "First", Race = "Human", Class = "Warrior", MaxDepth = 25, Level = 22, MaxLevel = 22, DateUtc = new DateTime(2026, 9, 1) },
            new ScoreEntry { Name = "Winner", Race = "Elf", Class = "Mage", MaxDepth = 100, Level = 50, MaxLevel = 50, Won = true, DateUtc = new DateTime(2026, 9, 25) },
        };
        var added = book.Backfill(entries, All, TestData.Game).Select(f => f.Id).ToList();
        Assert.True(book.Backfilled);
        Assert.Equal("First the Human Warrior", book.Earned["depth_20"].Character);   // the first to do it
        Assert.Equal("Later the Dwarf Priest", book.Earned["depth_40"].Character);
        Assert.Equal("Later the Dwarf Priest", book.Earned["level_30"].Character);
        Assert.Contains("win", added);
        Assert.Contains("win_mage", added);
        Assert.DoesNotContain("win_warrior", added);
        Assert.DoesNotContain("uniques_1", added);                                    // the scores don't say
    }

    [Fact]
    public void TheFirstFeatsFile_StillReads_AndTheNewOne_KeepsItsBackfill()
    {
        var path = Path.Combine(Path.GetTempPath(), $"avaband-feats-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "depth_10": { "WhenUtc": "2026-09-30T12:00:00Z", "Character": "Old" } }""");
            var old = FeatBook.Load(path);
            Assert.Equal("Old", old.Earned["depth_10"].Character);
            Assert.False(old.Backfilled);
            old.Backfilled = true;
            old.Save(path);
            var back = FeatBook.Load(path);
            Assert.True(back.Backfilled);
            Assert.Equal("Old", back.Earned["depth_10"].Character);
            File.WriteAllText(path, "[1, 2]");
            Assert.Empty(FeatBook.Load(path).Earned);
        }
        finally { File.Delete(path); }
    }
}
