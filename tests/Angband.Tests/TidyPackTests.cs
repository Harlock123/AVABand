using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>Game → Tidy pack: stacks that can now go together merged, and how full the pack is.</summary>
public class TidyPackTests
{
    [Fact]
    public void Stacks_kept_apart_merge_once_they_match()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        var a = game.Objects.Create("phase_door", 2);
        a.Note = "@r1";
        var b = game.Objects.Create("phase_door", 3);
        b.Note = "@r2";
        game.Player.Inventory.Add(a);
        game.Player.Inventory.Add(b);
        Assert.Equal(2, game.Player.Inventory.Pack.Count(i => i.Kind.Id == "phase_door")); // different inscriptions: apart
        var slots = game.Player.Inventory.SlotsUsed;

        b.Note = null; // uninscribed: they match now
        game.Execute(new TidyPackCommand());
        var stack = Assert.Single(game.Player.Inventory.Pack, i => i.Kind.Id == "phase_door");
        Assert.Equal(5, stack.Number);
        Assert.Equal(slots - 1, game.Player.Inventory.SlotsUsed);
        Assert.StartsWith("You tidy your pack: 1 stack merged.", said[^1]);
        Assert.Contains($"of {game.Player.Inventory.PackSize} slots used", said[^1]);

        game.Execute(new TidyPackCommand());
        Assert.StartsWith("Your pack is tidy.", said[^1]);
    }

    [Fact]
    public void Quest_items_are_never_called_junk()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        var quest = TestData.Game.Objects.Where(k => k.Flags.Contains("QUEST_ITEM")).ToList();
        Assert.NotEmpty(quest);
        foreach (var kind in quest) game.Player.Inventory.Add(game.Objects.Create(kind.Id));
        foreach (var item in game.Player.Inventory.Pack.Where(i => i.IsQuestItem)) game.Knowledge.LearnKind(item.Kind);

        Assert.DoesNotContain(game.JunkCandidates(), j => j.Item.IsQuestItem);  // (worth nothing to a shop, all the same)
        game.Execute(new TidyPackCommand());
        Assert.DoesNotContain("junk", said[^1]);
    }
}
