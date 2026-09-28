using Angband.Core.Definitions;
using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>Word of Recall's depth (Angband effect_handler_RECALL, player_get_recall_depth).</summary>
public class RecallDepthTests
{
    private static GameSession Game(bool persist)
    {
        var game = GameSession.NewGame(TestData.Game, 5, "warrior");
        game.Options[OptionIds.LevelsPersist] = persist;
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        return game;
    }

    private static void Stairs(GameSession game, bool down)
    {
        game.Player.Position = game.Level.FindFeature(down ? TerrainFlags.DownStair : TerrainFlags.UpStair).First();
        TestGames.ClearMonsters(game);
        Assert.True(game.Execute(new TakeStairsCommand(down)));
    }

    private static Angband.Core.Items.Item Scroll(GameSession game)
    {
        var scroll = game.Objects.Create("scroll_of_word_of_recall", 5);
        game.Knowledge.LearnKind(scroll.Kind);
        return game.Player.Inventory.Add(scroll)!;
    }

    private static void WaitForRecall(GameSession game)
    {
        for (var i = 0; i < 100 && game.Player.RecallTimer > 0; i++)
        {
            TestGames.ClearMonsters(game);
            game.Execute(new HoldCommand());
        }
    }

    [Fact]
    public void APersistentDungeon_RecallsToTheLevelChosen()
    {
        var game = Game(persist: true);
        Stairs(game, down: true);
        Stairs(game, down: true);
        Stairs(game, down: true);   // 3
        Stairs(game, down: false);
        Stairs(game, down: false);
        Stairs(game, down: false);  // town; levels 1-3 kept
        Assert.Equal([1, 2, 3], game.RecallChoices);
        var scroll = Scroll(game);
        Assert.True(game.StartsRecall(new UseCommand(scroll)));

        game.RecallChoice = 2;
        game.Execute(new UseCommand(scroll));
        Assert.Equal(2, game.Player.RecallDepth);
        WaitForRecall(game);
        Assert.Equal(2, game.Player.Depth);
    }

    [Fact]
    public void OnlyAVisitedLevel_WillDo()
    {
        var game = Game(persist: true);
        Stairs(game, down: true);
        Stairs(game, down: false);
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.RecallChoice = 7;
        game.Execute(new UseCommand(Scroll(game)));
        Assert.Contains("You must choose a level you have previously visited.", said);
        Assert.Equal(0, game.Player.RecallTimer);
    }

    [Fact]
    public void BelowTheDeepestLevel_YouMaySetTheRecallDepthHere()
    {
        var game = Game(persist: false);
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(10));
        game.Execute(new DebugJumpCommand(4));
        Assert.Equal(10, game.Player.MaxDepth);

        game.RecallSetsDepth = true;
        game.Execute(new UseCommand(Scroll(game)));
        Assert.Equal(4, game.Player.MaxDepth);
        Assert.Equal(4, game.Player.RecallDepth);
        WaitForRecall(game);
        Assert.Equal(0, game.Player.Depth);
        game.Execute(new UseCommand(Scroll(game)));
        WaitForRecall(game);
        Assert.Equal(4, game.Player.Depth);
    }

    [Fact]
    public void Saying_No_KeepsTheDeepestLevel()
    {
        var game = Game(persist: false);
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(10));
        game.Execute(new DebugJumpCommand(4));
        game.RecallSetsDepth = false;
        game.Execute(new UseCommand(Scroll(game)));
        Assert.Equal(10, game.Player.MaxDepth);
        WaitForRecall(game);
        game.Execute(new UseCommand(Scroll(game)));
        WaitForRecall(game);
        Assert.Equal(10, game.Player.Depth);
    }
}
