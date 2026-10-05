using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>Butterbur's strongroom: lockers shared by all your characters (AVABand's own).</summary>
public partial class AvaQuestTests
{
    /// <summary>Lockers in memory, as the app keeps them in a file.</summary>
    private sealed class MemoryLockers : IStrongroom
    {
        private readonly List<StrongroomLocker> _list = [];
        private int _next;
        public IReadOnlyList<StrongroomLocker> Lockers => [.. _list];
        public void Deposit(StrongroomLocker locker) => _list.Add(locker with { Id = $"L{++_next}", Date = "2026-10-05" });
        public bool Remove(string id) => _list.RemoveAll(l => l.Id == id) > 0;
    }

    private static Quester AtTheStrongroom(MemoryLockers lockers, int level = 20, ulong seed = 3)
    {
        var q = Start(level, seed);
        q.Game.Strongroom = lockers;
        q.EnterShop("inn");
        Reopen(q);
        return q;
    }

    /// <summary>Back into the strongroom (its menu drawn afresh, with what's carried now).</summary>
    private static void Reopen(Quester q)
    {
        q.EnterShop("inn");
        q.Choose("inn:strongroom");
    }

    private static Item Enchanted(Quester q)
    {
        var sword = q.Game.Objects.Create("long_sword");
        sword.ToHit = 7;
        sword.ToDam = 5;
        q.Game.Knowledge.LearnKind(sword.Kind);
        return q.Game.Player.Inventory.Add(sword)!;
    }

    [Fact]
    public void One_character_leaves_a_sword_and_the_next_takes_it_as_it_was()
    {
        var lockers = new MemoryLockers();
        var first = AtTheStrongroom(lockers);
        Assert.Equal("The strongroom", first.Last.Title);
        var sword = Enchanted(first);
        var fee = first.Game.StrongroomDepositFee(sword);
        Assert.Equal(50 + ItemValue.Of(sword, first.Game.Data) / 20, fee);
        first.Game.Player.Gold = fee;
        Reopen(first);                                                            // (the menu again, with the sword in it)
        first.Choose($"strongroom:store:{sword.Serial}");
        Assert.Equal(0, first.Game.Player.Gold);
        Assert.False(first.Game.Player.Inventory.Contains(sword));
        var locker = Assert.Single(lockers.Lockers);
        Assert.Contains("Long Sword", locker.Description);
        Assert.StartsWith($"{first.Game.Player.Name} the ", locker.LeftBy);

        // Another character: the locker is in their strongroom too.
        var next = AtTheStrongroom(lockers, seed: 9);
        Assert.Contains(next.Last.Choices, c => c.Id == $"strongroom-take:{locker.Id}");
        var withdraw = GameSession.StrongroomWithdrawFee(locker.Value);
        next.Game.Player.Gold = withdraw;
        next.Game.Execute(new TakeFromLockerCommand(locker.Id, locker.ItemJson, locker.Value));
        var back = next.Game.Player.Inventory.Pack.Single(i => i.Kind.Id == "long_sword");
        Assert.Equal((7, 5), (back.ToHit, back.ToDam));
        Assert.Equal(0, next.Game.Player.Gold);
        Assert.Empty(lockers.Lockers);
    }

    [Fact]
    public void A_character_too_far_behind_must_wait_for_it()
    {
        var lockers = new MemoryLockers();
        var first = AtTheStrongroom(lockers, level: 30);
        var deep = first.Game.Player.Inventory.Add(first.Game.Objects.Create("executioners_sword"))!;
        first.Game.Player.Gold = 100_000;
        Reopen(first);
        first.Choose($"strongroom:store:{deep.Serial}");
        var locker = Assert.Single(lockers.Lockers);
        Assert.True(locker.ItemLevel > 7);

        var young = AtTheStrongroom(lockers, level: 1, seed: 9);
        Assert.Contains(young.Last.Choices, c => c.Label.Contains("you can take this at level"));
        young.Game.Player.Gold = 100_000;
        young.Game.Execute(new TakeFromLockerCommand(locker.Id, locker.ItemJson, locker.Value));
        Assert.Contains(young.Said, s => s.Contains("Not yet"));
        Assert.Single(lockers.Lockers);
        Assert.DoesNotContain(young.Game.Player.Inventory.Pack, i => i.Kind.Id == "executioners_sword");
    }

    [Fact]
    public void No_artifacts_no_quest_items_and_six_lockers_at_most()
    {
        var lockers = new MemoryLockers();
        var q = AtTheStrongroom(lockers);
        var game = q.Game;
        var artifact = game.Player.Inventory.Add(game.Objects.CreateArtifact(game.Data.Artifacts.First(a => a.Id == "boots_of_strider")))!;
        Assert.NotNull(game.StrongroomRefusal(artifact));
        Assert.NotNull(game.StrongroomRefusal(game.Player.Inventory.Add(game.Objects.Create("palantir"))!));
        game.Player.Gold = 1_000_000;
        for (var i = 0; i < GameSession.StrongroomLockers; i++)
        {
            var dagger = game.Player.Inventory.Add(game.Objects.Create("dagger"))!;
            Reopen(q);
            q.Choose($"strongroom:store:{dagger.Serial}");
        }
        Assert.Equal(GameSession.StrongroomLockers, lockers.Lockers.Count);
        Reopen(q);
        Assert.DoesNotContain(q.Last.Choices, c => c.Id.StartsWith("strongroom:store:", StringComparison.Ordinal)); // full
    }

    /// <summary>A replay has no lockers file: the take carries its item, and works without one.</summary>
    [Fact]
    public void Taking_out_works_from_the_command_alone_as_in_a_replay()
    {
        var lockers = new MemoryLockers();
        var first = AtTheStrongroom(lockers);
        var sword = Enchanted(first);
        first.Game.Player.Gold = 100_000;
        Reopen(first);
        first.Choose($"strongroom:store:{sword.Serial}");
        var locker = lockers.Lockers[0];

        var replay = Start(seed: 9);
        replay.EnterShop("inn");                                                 // (no strongroom given)
        replay.Game.Player.Gold = 100_000;
        replay.Game.Execute(new TakeFromLockerCommand(locker.Id, locker.ItemJson, locker.Value));
        Assert.Contains(replay.Game.Player.Inventory.Pack, i => i.Kind.Id == "long_sword" && i.ToHit == 7);
    }

    [Fact]
    public void An_item_keeps_its_gems_through_a_locker_with_fresh_serials()
    {
        var q = Start();
        var bracers = q.Game.Objects.Create("leather_bracers");
        bracers.Gems.Add(q.Game.Objects.Create("ruby"));
        var json = SaveGame.ItemToJson(bracers);
        var back = SaveGame.ItemFromJson(q.Game, json)!;
        Assert.Equal("ruby", Assert.Single(back.Gems).Kind.Id);
        Assert.NotEqual(bracers.Serial, back.Serial);
        Assert.Null(SaveGame.ItemFromJson(q.Game, "not json"));
    }

    [Fact]
    public void Without_the_birth_option_or_for_a_daily_character_there_is_no_strongroom()
    {
        var q = Start();
        q.Game.Strongroom = new MemoryLockers();
        q.EnterShop("inn");
        Assert.Contains(q.Last.Choices, c => c.Id == "inn:strongroom");
        q.Game.Options[OptionIds.Strongroom] = false;
        q.EnterShop("inn");
        Assert.DoesNotContain(q.Last.Choices, c => c.Id == "inn:strongroom");
        q.Game.Options[OptionIds.Strongroom] = true;
        q.Game.Player.DailyDate = "2026-10-05";
        q.EnterShop("inn");
        Assert.DoesNotContain(q.Last.Choices, c => c.Id == "inn:strongroom");
    }
}
