using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>The tutorial level: each lesson, in order, then the stairs.</summary>
public class TutorialTests
{
    [Fact]
    public void TheTutorial_CanBePlayedThrough_StepByStep()
    {
        var game = Tutorial.Create(TestData.Game);
        Assert.True(game.IsTutorial);
        Assert.Equal(1, game.Player.Depth);
        Assert.Equal(TutorialStep.PickUp, Tutorial.StepOf(game));

        // Pick up the potion.
        var potion = game.Level.Objects.All.Single(o => o.Item.Kind.Id == Tutorial.PotionId).Loc;
        while (game.Player.Position != potion) game.Execute(new WalkCommand(Direction.East));
        if (game.Level.Objects.Any(potion)) game.Execute(new PickupCommand());
        Assert.Equal(TutorialStep.OpenDoor, Tutorial.StepOf(game));

        // Open the door (walking into it, as the tutorial says).
        for (var i = 0; i < 20 && Tutorial.StepOf(game) == TutorialStep.OpenDoor; i++) game.Execute(new WalkCommand(Direction.East));
        Assert.Equal(TutorialStep.Trap, Tutorial.StepOf(game));

        // Past the trap: walk at it until it is disarmed (or jumped).
        for (var i = 0; i < 400 && Tutorial.StepOf(game) == TutorialStep.Trap; i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.Execute(new WalkCommand(Direction.East));
        }
        Assert.Equal(TutorialStep.Fight, Tutorial.StepOf(game));

        // The kobold.
        var kobold = game.Level.Monsters.All.Single(m => m.Race.Id == Tutorial.MonsterId);
        game.DamageMonster(kobold, 10_000);
        Assert.Equal(TutorialStep.Recall, Tutorial.StepOf(game));

        // Word of Recall: read, it says what it would do — and the tutorial stays on its level.
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        var scrollAt = game.Level.Objects.All.Single(o => o.Item.Kind.Id == Tutorial.ScrollId).Loc;
        game.Player.Position = scrollAt;
        game.Execute(new PickupCommand());
        var scroll = game.Player.Inventory.Pack.First(i => i.Kind.Id == Tutorial.ScrollId && i.Number >= 1);
        game.Execute(new UseCommand(scroll));
        Assert.Contains(messages, m => m.Contains("In a real game"));
        Assert.Equal(0, game.Player.RecallTimer);
        Assert.Equal(TutorialStep.Inn, Tutorial.StepOf(game));

        // The Prancing Pony's door: a lesson on quests, not the real inn.
        var prompts = new List<QuestPromptEvent>();
        game.Events.Subscribe<QuestPromptEvent>(prompts.Add);
        game.Player.Position = game.Level.AllLocs().Single(p => game.Level.FeatureAt(p).Shop == "inn");
        game.Execute(new EnterStoreCommand());
        Assert.Equal("The Prancing Pony (a lesson)", prompts.Last().Title);
        Assert.Empty(game.AvaQuests.Quests);
        Assert.Equal(TutorialStep.Knowledge, Tutorial.StepOf(game));

        // The Knowledge screen (the interface marks it), then the stairs.
        game.TutorialDone.Add("knowledge");
        Assert.Equal(TutorialStep.Stairs, Tutorial.StepOf(game));
        Assert.Contains(game.Level.AllLocs(), p => game.Level.Has(p, Angband.Core.Definitions.TerrainFlags.DownStair));
    }
}
