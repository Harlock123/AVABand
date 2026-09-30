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

    [Fact]
    public void The_deep_quests_have_feats_of_their_own()
    {
        var game = Game();
        var book = new FeatBook();
        game.AvaQuests.Quests["heart"] = new AvaQuestState { Id = "heart", Stage = "kept" };
        game.AvaQuests.Quests["watch"] = new AvaQuestState { Id = "watch", Stage = "relieved", Numbers = { ["held"] = 80 } }; // Grishnag slain
        game.AvaQuests.Quests["stone"] = new AvaQuestState { Id = "stone", Stage = "keep", Numbers = { ["looks"] = 9 } };
        var ids = book.Check(game, All).Select(f => f.Id).ToList();
        Assert.DoesNotContain("heart_returned", ids);
        Assert.DoesNotContain("watch_held", ids);
        Assert.DoesNotContain("stone_kept", ids);

        game.AvaQuests.Quests["heart"].Stage = "returned";
        game.AvaQuests.Quests["watch"].Numbers["held"] = GameSession.WatchTurns;
        game.AvaQuests.Quests["stone"].Numbers["looks"] = 10;
        Assert.Equal(["heart_returned", "watch_held", "stone_kept"], book.Check(game, All).Select(f => f.Id).Where(id => id is "heart_returned" or "watch_held" or "stone_kept"));
    }

    [Fact]
    public void Looking_into_the_palantir_is_counted()
    {
        var game = Game();
        game.AvaQuests.Quests["stone"] = new AvaQuestState { Id = "stone", Stage = "keep" };
        var stone = game.Objects.Create("palantir");
        game.Player.Inventory.Add(stone);
        for (var i = 0; i < 3; i++) game.Execute(new UseCommand(game.Player.Inventory.Pack.First(p => p.Kind.Id == "palantir")));
        Assert.Equal(3, game.AvaQuests.Get("stone")!.N("looks"));
    }

    [Fact]
    public void The_daily_dungeon_has_feats_from_its_board()
    {
        var board = new DailyBoard();
        var book = new FeatBook();
        Assert.Empty(book.CheckBoard(board, All));
        foreach (var day in new[] { "2026-09-01", "2026-09-02", "2026-09-03", "2026-09-05", "2026-09-06", "2026-09-07", "2026-09-08", "2026-09-09", "2026-09-10" })
            board.Add(new DailyEntry { Day = day, Name = "Dain", Race = "Dwarf", Class = "Priest", DateUtc = DateTime.Parse(day + "T12:00:00Z").ToUniversalTime() });
        Assert.Equal(6, Feats.DailyStreak(board)); // (the 4th missed)
        Assert.Equal(["daily"], book.CheckBoard(board, All).Select(f => f.Id));
        Assert.Equal("Dain the Dwarf Priest", book.Earned["daily"].Character);

        board.Add(new DailyEntry { Day = "2026-09-11", Name = "Dain", Race = "Dwarf", Class = "Priest", DateUtc = DateTime.UtcNow });
        Assert.Equal(7, Feats.DailyStreak(board));
        Assert.Equal(["daily_week"], book.CheckBoard(board, All).Select(f => f.Id));
        Assert.Empty(book.CheckBoard(board, All));
    }

    [Fact]
    public void Going_deep_in_a_daily_dungeon_is_a_feat()
    {
        var game = Game();
        game.Player.MaxDepth = 20;
        Assert.DoesNotContain("daily_deep", new FeatBook().Check(game, All).Select(f => f.Id));
        game.Player.DailyDate = "2026-09-30";
        Assert.Contains("daily_deep", new FeatBook().Check(game, All).Select(f => f.Id));
    }
}
