using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Quests;

namespace Angband.Tests;

/// <summary>The notice board's parcels and rescues (AVABand's own).</summary>
public partial class AvaQuestTests
{
    private static BoardJob TakeJob(Quester q, BoardJob job)
    {
        q.Game.AvaQuests.Board.Add(job);
        q.EnterShop("inn");
        q.Choose("inn:board");
        q.Choose($"board:take:{job.Id}");
        Assert.True(job.Taken);
        return job;
    }

    [Fact]
    public void A_parcel_is_carried_to_its_shop_and_paid_for_at_the_inn()
    {
        var q = Start(level: 10);
        var game = q.Game;
        var job = TakeJob(q, new BoardJob { Id = 901, Kind = "parcel", Target = "alchemist", Count = 1, Reward = 90 });
        var parcel = Assert.Single(game.Player.Inventory.Satchel, i => i.Kind.Id == "sealed_parcel");
        Assert.Equal("Deliver a parcel to the Alchemy shop", game.JobTitle(job));

        q.EnterShop("alchemist");
        Assert.Equal(1, job.Progress);
        Assert.DoesNotContain(parcel, game.Player.Inventory.Pack);
        Assert.Contains(q.Said, s => s.Contains("Tell him it came safe"));

        var gold = game.Player.Gold;
        q.EnterShop("inn");
        q.Choose($"board:collect:{job.Id}");
        Assert.Equal(gold + 90, game.Player.Gold);
    }

    [Fact]
    public void Giving_up_a_parcel_gives_the_parcel_back()
    {
        var q = Start(level: 10);
        var job = TakeJob(q, new BoardJob { Id = 902, Kind = "parcel", Target = "magic", Count = 1, Reward = 90 });
        q.Choose($"board:drop:{job.Id}");
        Assert.DoesNotContain(q.Game.Player.Inventory.Pack, i => i.Kind.Id == "sealed_parcel");
    }

    [Fact]
    public void A_traveller_trapped_at_a_depth_is_found_freed_and_paid_for()
    {
        var q = Start(level: 10);
        var game = q.Game;
        var job = TakeJob(q, new BoardJob { Id = 903, Kind = "rescue", Target = "3", Count = 3, Reward = 240 });
        Assert.True(job.Deadline > game.GameTurn);

        q.Jump(3);
        Assert.Contains(q.Said, s => s.Contains("someone is crying for help"));
        var at = new Loc(job.AtX, job.AtY);
        Assert.Equal("trapped_apprentice", game.Level.FeatureAt(at).Id);
        q.WalkInto(at);
        Assert.Equal(1, job.Progress);
        Assert.Contains(q.Said, s => s.StartsWith("You free the trapped traveller", StringComparison.Ordinal));
        Assert.NotEqual("trapped_apprentice", game.Level.FeatureAt(at).Id);

        var gold = game.Player.Gold;
        q.EnterShop("inn");
        q.Choose($"board:collect:{job.Id}");
        Assert.Equal(gold + 240, game.Player.Gold);
    }

    [Fact]
    public void Too_late_for_a_rescue_and_its_note_is_crossed_out()
    {
        var q = Start(level: 10);
        var game = q.Game;
        var job = TakeJob(q, new BoardJob { Id = 904, Kind = "rescue", Target = "3", Count = 3, Reward = 240 });
        game.Scheduler.SetGameTurn(job.Deadline + 1);
        q.Jump(3);
        Assert.DoesNotContain(job, game.AvaQuests.Board);
        Assert.Contains(q.Said, s => s.StartsWith("Too late", StringComparison.Ordinal));
        Assert.DoesNotContain(game.Level.AllLocs(), p => game.Level.FeatureAt(p).Id == "trapped_apprentice");
    }
}
