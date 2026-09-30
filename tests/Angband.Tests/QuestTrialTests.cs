using Angband.Core.Game;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>Debug → Try a quest: a character raised, kitted, holding the quest, on its level.</summary>
public class QuestTrialTests
{
    public static TheoryData<string> Quests() => [.. GameSession.QuestTrialLevels.Keys];

    [Theory]
    [MemberData(nameof(Quests))]
    public void A_quest_can_be_tried_at_once(string quest)
    {
        var game = GameSession.NewGame(TestData.Game, 7, "warrior");
        Assert.True(game.Execute(new DebugTryQuestCommand(quest)));
        Assert.True(game.UsedDebug);
        Assert.Equal(GameSession.QuestTrialLevels[quest], game.Player.Level);
        Assert.True(game.Player.Depth > 0);
        if (quest != "burden") Assert.NotNull(game.AvaQuests.Get(quest));
        Assert.NotNull(game.Player.Inventory.Weapon);
        Assert.True(game.Player.Inventory.Equipped.Count() >= 6, $"{game.Player.Inventory.Equipped.Count()} worn");
        Assert.Contains(game.Player.Inventory.Pack, i => i.Kind.Id == "phase_door");
        Assert.Equal(game.Player.MaxHp, game.Player.Hp);
        Assert.DoesNotContain(game.Player.Inventory.Pack, i => i.IsWearable && i.Kind.Id is "dagger" or "soft_leather_armour");
        Assert.True(game.Player.Inventory.TotalWeight <= game.Player.WeightLimit / 2, $"{game.Player.Inventory.TotalWeight} of {game.Player.WeightLimit / 2} carried");
    }

    [Fact]
    public void The_deep_quests_boss_is_there_when_you_arrive()
    {
        var game = GameSession.NewGame(TestData.Game, 7, "warrior");
        game.Execute(new DebugTryQuestCommand("heart"));
        Assert.Contains(game.Level.Monsters.All, m => m.Race.Id == "skorvath_the_cold_drake");
    }

    [Fact]
    public void A_caster_comes_with_its_books_and_spells()
    {
        var game = GameSession.NewGame(TestData.Game, 7, "mage");
        game.Execute(new DebugTryQuestCommand("warden"));
        Assert.True(game.Player.LearnedSpells.Count >= 10, $"{game.Player.LearnedSpells.Count} spells");
        Assert.Null(game.Player.Inventory.InSlot(Angband.Core.Definitions.EquipSlot.Hands)); // (no gloves to hamper it)
    }

    [Fact]
    public void A_quest_already_held_is_not_started_again()
    {
        var game = GameSession.NewGame(TestData.Game, 7, "warrior");
        game.Execute(new DebugTryQuestCommand("letter"));
        Assert.False(game.Execute(new DebugTryQuestCommand("letter")));
        Assert.False(game.Execute(new DebugTryQuestCommand("no_such_quest")));
    }

    [Fact]
    public void A_trial_plays_back_from_its_replay()
    {
        var game = GameSession.NewGame(TestData.Game, 7, "priest");
        game.Recorder = new ReplayRecorder(game);
        game.Execute(new DebugTryQuestCommand("consecration"));
        for (var i = 0; i < 30; i++) game.Execute(new HoldCommand());
        var player = new ReplayPlayer(TestData.Game, game.Recorder.ToFile(game));
        while (player.Step()) { }
        Assert.True(player.Matches);
        Assert.Equal(game.Player.Inventory.All.Select(i => i.Kind.Id), player.Game.Player.Inventory.All.Select(i => i.Kind.Id));
    }
}
