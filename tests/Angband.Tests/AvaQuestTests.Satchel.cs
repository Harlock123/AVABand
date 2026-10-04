using Angband.Core.Game;
using Angband.Core.Quests;

namespace Angband.Tests;

/// <summary>Quest items and room to carry them: the quest satchel, and "bring" jobs handed in bit by bit or kept at home.</summary>
public partial class AvaQuestTests
{
    [Fact]
    public void Quest_items_ride_in_the_satchel_and_take_no_pack_slot()
    {
        var q = Start();
        var inv = q.Game.Player.Inventory;
        var before = inv.SlotsUsed;
        foreach (var kind in new[] { "shard_of_the_hilt", "shard_of_the_blade", "shard_of_the_point", "journal_page", "palantir" })
            Assert.NotNull(inv.Add(q.Game.Objects.Create(kind)));
        Assert.Equal(before, inv.SlotsUsed);
        Assert.Equal(5, inv.Satchel.Count());
        Assert.All(inv.Pack.TakeLast(5), i => Assert.True(i.IsQuestItem));       // last, after the pack's own things
        Assert.True(inv.TotalWeight > 0);

        // A full pack still takes another quest item, and an overflowing one never spills them.
        foreach (var kind in q.Game.Data.Objects.Where(k => k.Base is "scroll" or "potion").Take(40))
            if (inv.SlotsUsed < inv.PackSize) inv.Add(q.Game.Objects.Create(kind.Id));
        Assert.True(inv.CanCarry(q.Game.Objects.Create("wardens_taper")));
        Assert.Equal(5, inv.Satchel.Count());
    }

    private static BoardJob BringJob(Quester q, int count = 4)
    {
        var kind = q.Game.Data.Objects.First(k => k.Base == "potion" && k.Cost > 0).Id;
        var job = new BoardJob { Id = 900, Kind = "gather", Target = kind, Count = count, Reward = 300, Taken = true };
        q.Game.AvaQuests.Board.Add(job);
        return job;
    }

    private static void Carry(Quester q, string kind, int n) => q.Game.Player.Inventory.Add(q.Game.Objects.Create(kind, n));

    [Fact]
    public void A_bring_job_can_be_handed_in_a_few_at_a_time()
    {
        var q = Start(level: 10);
        var game = q.Game;
        var job = BringJob(q);
        Carry(q, job.Target, 1);
        q.EnterShop("inn");
        var deliver = Assert.Single(q.Last.Choices, c => c.Id == $"board:deliver:{job.Id}");
        Assert.EndsWith("(1 of 4)", deliver.Label);
        q.Choose(deliver.Id);
        Assert.Equal(1, job.Progress);
        Assert.Equal(0, game.Player.Inventory.Pack.Where(i => i.Kind.Id == job.Target).Sum(i => i.Number));
        Assert.Contains(q.Said, s => s.Contains("3 more and the pay's yours"));
        Assert.Contains(job, game.AvaQuests.Board);
        Assert.Contains("1 brought in", game.JobText(job));

        // The rest, and the pay.
        var gold = game.Player.Gold;
        Carry(q, job.Target, 3);
        q.EnterShop("inn");
        q.Choose($"board:collect:{job.Id}");
        Assert.Equal(gold + 300, game.Player.Gold);
        Assert.DoesNotContain(job, game.AvaQuests.Board);
        Assert.Equal(0, game.Player.Inventory.Pack.Where(i => i.Kind.Id == job.Target).Sum(i => i.Number));
    }

    [Fact]
    public void What_you_keep_at_home_counts_and_is_taken_from_there()
    {
        var q = Start(level: 10);
        var game = q.Game;
        var job = BringJob(q, count: 3);
        var home = game.Stores.Values.Single(s => s.IsHome);
        home.Stock.Add(game.Objects.Create(job.Target, 2));
        Carry(q, job.Target, 1);
        Assert.Equal(3, game.GatherOnHand(job));
        var entry = game.QuestLog().Single(e => e.Name == game.JobTitle(job));
        Assert.Equal((3, 3), (entry.Have, entry.Need));
        Assert.StartsWith("Ready", entry.Status);
        Assert.Contains("1 carried, 2 at home", game.JobText(job));

        var gold = game.Player.Gold;
        q.EnterShop("inn");
        q.Choose($"board:collect:{job.Id}");                                    // the carried one first, then the home's
        Assert.Equal(gold + 300, game.Player.Gold);
        Assert.DoesNotContain(home.Stock, i => i.Kind.Id == job.Target);
        Assert.Equal(0, game.Player.Inventory.Pack.Where(i => i.Kind.Id == job.Target).Sum(i => i.Number));
    }

    [Fact]
    public void Handing_in_takes_only_what_the_job_still_needs()
    {
        var q = Start(level: 10);
        var game = q.Game;
        var job = BringJob(q, count: 2);
        job.Progress = 1;
        Carry(q, job.Target, 3);
        q.EnterShop("inn");
        q.Choose($"board:collect:{job.Id}");
        Assert.Equal(2, game.Player.Inventory.Pack.Where(i => i.Kind.Id == job.Target).Sum(i => i.Number)); // one taken, two kept
    }
}
