using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>The gem pouch (one pack slot for every gem) and the Arcane Artificer's gem cutting.</summary>
public partial class AvaQuestTests
{
    private static Item Gem(Quester q, string kind, int n = 1)
    {
        var gem = q.Game.Objects.Create(kind, n);
        q.Game.Knowledge.LearnKind(gem.Kind);
        return q.Game.Player.Inventory.Add(gem)!;
    }

    private static int GemsOf(Quester q, string kind) => q.Game.Player.Inventory.Pouch.Where(i => i.Kind.Id == kind).Sum(i => i.Number);

    [Fact]
    public void All_the_gems_together_take_one_pack_slot()
    {
        var q = Start();
        var inv = q.Game.Player.Inventory;
        var before = inv.SlotsUsed;
        Gem(q, "ruby");
        Assert.Equal(before + 1, inv.SlotsUsed);                               // the pouch's one slot
        foreach (var kind in new[] { "chipped_sapphire", "emerald", "flawed_topaz", "diamond", "bloodstone" }) Gem(q, kind);
        Assert.Equal(before + 1, inv.SlotsUsed);                               // however many kinds
        Assert.Equal(6, inv.Pouch.Count());

        // Order: the pack's own things, the pouch, the satchel.
        inv.Add(q.Game.Objects.Create("palantir"));
        var ranks = inv.Pack.Select(BagRank).ToList();
        Assert.Equal(ranks.Order().ToList(), ranks);

        // A full pack still takes another gem, the pouch being carried; it overflows the pack's things, not gems.
        foreach (var kind in q.Game.Data.Objects.Where(k => k.Base is "scroll" or "potion").Take(40))
            if (inv.SlotsUsed < inv.PackSize) inv.Add(q.Game.Objects.Create(kind.Id));
        Assert.True(inv.CanCarry(q.Game.Objects.Create("opal")));
    }

    [Fact]
    public void A_gem_says_what_it_does_in_a_socket()
    {
        var q = Start();
        Assert.Equal("+3 to-dam, resist fire", q.Game.GemEffectText(Gem(q, "ruby")));
        Assert.Equal("+12 armour", q.Game.GemEffectText(Gem(q, "diamond")));
        Assert.Contains("cursed:", q.Game.GemEffectText(Gem(q, "bloodstone")));
    }

    [Fact]
    public void The_artificer_cuts_three_gems_into_one_of_the_next_grade()
    {
        var q = Start(level: 10);
        var game = q.Game;
        Gem(q, "chipped_ruby", 4);
        Gem(q, "bloodstone", 3);                                               // (no finer grade: not offered)
        var fee = game.GemCuttingFee("flawed_ruby");
        Assert.Equal(game.Data.Object("flawed_ruby")!.Cost / 5, fee);
        game.Player.Gold = fee;
        q.EnterShop("artificer");
        q.Choose("artificer:gems");
        Assert.Contains(q.Last.Choices, c => c.Id == "artificer:cutgem:chipped_ruby");
        Assert.DoesNotContain(q.Last.Choices, c => c.Id.EndsWith("bloodstone", StringComparison.Ordinal));
        q.Choose("artificer:cutgem:chipped_ruby");
        Assert.Equal(1, GemsOf(q, "chipped_ruby"));
        Assert.Equal(1, GemsOf(q, "flawed_ruby"));
        Assert.Equal(0, game.Player.Gold);

        // Three flawed into a whole one — but not without the gold.
        Gem(q, "flawed_ruby", 2);
        q.Choose("artificer:menu");
        q.Choose("artificer:gems");                                            // (the menu again, with three now)
        q.Choose("artificer:cutgem:flawed_ruby");
        Assert.Equal(3, GemsOf(q, "flawed_ruby"));
        Assert.Contains(q.Said, s => s.Contains("Come back when you have it"));
        game.Player.Gold = game.GemCuttingFee("ruby");
        q.Choose("artificer:cutgem:flawed_ruby");
        Assert.Equal(1, GemsOf(q, "ruby"));
        Assert.Equal(0, GemsOf(q, "flawed_ruby"));
    }

    [Fact]
    public void The_artificer_offers_cutting_only_to_someone_with_gems()
    {
        var q = Start(level: 10);
        q.EnterShop("artificer");
        Assert.DoesNotContain(q.Last.Choices, c => c.Id == "artificer:gems");
        Gem(q, "chipped_opal");
        q.EnterShop("artificer");
        Assert.Contains(q.Last.Choices, c => c.Id == "artificer:gems");
    }
}
