using System.IO.Compression;
using System.Text.Json.Nodes;
using Angband.Core.Game;
using Angband.Core.Persistence;
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

    /// <summary>
    /// A save from before the satchel: its pack in the old order, a quest item among the potions and
    /// scrolls, and every slot full. Loaded, the quest items move to the satchel and give their slots back.
    /// </summary>
    [Fact]
    public void An_older_saves_quest_items_go_into_the_satchel_on_loading()
    {
        var q = Start();
        var inv = q.Game.Player.Inventory;
        foreach (var kind in new[] { "shard_of_the_hilt", "journal_page" }) inv.Add(q.Game.Objects.Create(kind));
        foreach (var kind in q.Game.Data.Objects.Where(k => k.Base is "scroll" or "potion").Take(40))
            if (inv.SlotsUsed < inv.PackSize) inv.Add(q.Game.Objects.Create(kind.Id));

        // Write it, then put the quest items back where the old order had them (by base: "quest" before
        // "scroll"), as a save made before the satchel would.
        using var stream = new MemoryStream();
        SaveGame.Save(q.Game, stream);
        stream.Position = 0;
        JsonNode file;
        using (var unzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true)) file = JsonNode.Parse(unzip)!;
        var pack = FindArray(file, "Pack")!;
        var quest = pack.Where(n => n!["Kind"]?.GetValue<string>() is "shard_of_the_hilt" or "journal_page").ToList();
        Assert.Equal(2, quest.Count);
        foreach (var n in quest) pack.Remove(n);
        var firstScroll = pack.Select((n, i) => (n, i)).First(t => t.n!["Kind"]!.GetValue<string>().StartsWith("scroll", StringComparison.Ordinal)
                                                                   || q.Game.Data.Object(t.n["Kind"]!.GetValue<string>())?.Base == "scroll").i;
        foreach (var n in quest) pack.Insert(firstScroll, n!.DeepClone());
        var old = new MemoryStream();
        using (var zip = new GZipStream(old, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new System.Text.Json.Utf8JsonWriter(zip)) file.WriteTo(writer);
        old.Position = 0;

        var loaded = SaveGame.Load(TestData.Game, old).Player.Inventory;
        Assert.Equal(["journal_page", "shard_of_the_hilt"], loaded.Satchel.Select(i => i.Kind.Id).Order());
        Assert.All(loaded.Pack.TakeLast(2), i => Assert.True(i.IsQuestItem));     // moved to the end
        Assert.Equal(inv.SlotsUsed, loaded.SlotsUsed);                             // and taking no slots
        Assert.True(loaded.SlotsUsed <= loaded.PackSize);
    }

    /// <summary>
    /// A save from before the gem pouch: gems among the potions and scrolls, each kind its own slot, the
    /// pack full. Loaded, the gems go into the pouch (after the pack's own things, before the satchel), all
    /// of them in one slot, and nothing is lost.
    /// </summary>
    [Fact]
    public void An_older_saves_gems_go_into_the_pouch_on_loading()
    {
        var q = Start();
        var inv = q.Game.Player.Inventory;
        string[] gems = ["ruby", "chipped_sapphire", "emerald", "flawed_topaz"];
        foreach (var kind in gems) inv.Add(q.Game.Objects.Create(kind));
        inv.Add(q.Game.Objects.Create("journal_page"));
        foreach (var kind in q.Game.Data.Objects.Where(k => k.Base is "scroll" or "potion").Take(40))
            if (inv.SlotsUsed < inv.PackSize) inv.Add(q.Game.Objects.Create(kind.Id));
        var carried = inv.Pack.Sum(i => i.Number);

        // Put the gems back where the old order had them: by base ("gem" before "potion"), first.
        using var stream = new MemoryStream();
        SaveGame.Save(q.Game, stream);
        stream.Position = 0;
        JsonNode file;
        using (var unzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true)) file = JsonNode.Parse(unzip)!;
        var pack = FindArray(file, "Pack")!;
        var gemNodes = pack.Where(n => gems.Contains(n!["Kind"]?.GetValue<string>())).ToList();
        Assert.Equal(gems.Length, gemNodes.Count);
        foreach (var n in gemNodes) pack.Remove(n);
        foreach (var n in gemNodes.AsEnumerable().Reverse()) pack.Insert(0, n!.DeepClone());
        var old = new MemoryStream();
        using (var zip = new GZipStream(old, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new System.Text.Json.Utf8JsonWriter(zip)) file.WriteTo(writer);
        old.Position = 0;

        var loaded = SaveGame.Load(TestData.Game, old).Player.Inventory;
        Assert.Equal(gems.Order(), loaded.Pouch.Select(i => i.Kind.Id).Order());
        var ranks = loaded.Pack.Select(i => i.IsQuestItem ? 2 : Angband.Core.Items.Inventory.InPouch(i) ? 1 : 0).ToList();
        Assert.Equal(ranks.Order().ToList(), ranks);                               // pack, pouch, satchel
        Assert.Equal(carried, loaded.Pack.Sum(i => i.Number));                     // nothing lost
        Assert.Equal(inv.SlotsUsed, loaded.SlotsUsed);
        Assert.True(loaded.SlotsUsed <= loaded.PackSize);
    }

    private static JsonArray? FindArray(JsonNode? node, string name) => node switch
    {
        JsonObject o => o.Select(kv => kv.Key == name && kv.Value is JsonArray a ? a : FindArray(kv.Value, name)).FirstOrDefault(a => a is not null),
        JsonArray arr => arr.Select(n => FindArray(n, name)).FirstOrDefault(a => a is not null),
        _ => null,
    };
}
