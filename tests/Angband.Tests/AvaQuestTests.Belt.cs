using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>The potion belt and the scroll case (once earned): up to eight kinds each share one pack slot.</summary>
public partial class AvaQuestTests
{
    /// <summary>Both holders, as their quests give them.</summary>
    private static void GiveHolders(Quester q)
    {
        q.Game.Player.Inventory.Add(q.Game.Objects.Create(Inventory.BeltKind));
        q.Game.Player.Inventory.Add(q.Game.Objects.Create(Inventory.CaseKind));
    }

    private static string[] KindsOf(Quester q, string @base, int count) =>
        [.. q.Game.Data.Objects.Where(k => k.Base == @base && !k.Flags.Contains("QUEST_ITEM")).Select(k => k.Id).Take(count)];

    [Theory]
    [InlineData("potion")]
    [InlineData("scroll")]
    public void Up_to_eight_kinds_share_one_slot_and_each_past_that_takes_its_own(string @base)
    {
        var q = Start();
        GiveHolders(q);
        var inv = q.Game.Player.Inventory;
        foreach (var held in inv.Pack.Where(i => i.Base.Id == @base).ToList())          // (start with an empty holder)
            inv.Remove(held, held.Number, () => q.Game.Objects.NextSerial++);
        var kinds = KindsOf(q, @base, 10);
        Assert.Equal(10, kinds.Length);
        var before = inv.SlotsUsed;
        inv.Add(q.Game.Objects.Create(kinds[0]));
        Assert.Equal(before + 1, inv.SlotsUsed);                               // the holder's one slot
        foreach (var kind in kinds.Skip(1).Take(7)) inv.Add(q.Game.Objects.Create(kind));
        Assert.Equal(before + 1, inv.SlotsUsed);                               // eight kinds, still one
        inv.Add(q.Game.Objects.Create(kinds[8]));
        Assert.Equal(before + 2, inv.SlotsUsed);                               // the ninth takes a slot
        inv.Add(q.Game.Objects.Create(kinds[9]));
        Assert.Equal(before + 3, inv.SlotsUsed);

        var ranks = inv.Pack.Select(inv.Rank).ToList();
        Assert.Equal(ranks.Order().ToList(), ranks);                          // pack, belt, case, books, gems, satchel
    }

    [Fact]
    public void A_full_pack_takes_another_kind_only_while_the_holder_has_room()
    {
        var q = Start();
        GiveHolders(q);
        var inv = q.Game.Player.Inventory;
        foreach (var held in inv.Pack.Where(i => i.Base.Id is "potion" or "scroll").ToList())
            inv.Remove(held, held.Number, () => q.Game.Objects.NextSerial++);
        var potions = KindsOf(q, "potion", 9);
        foreach (var kind in potions.Take(3)) inv.Add(q.Game.Objects.Create(kind));
        foreach (var kind in q.Game.Data.Objects.Where(k => k.Base is "food" or "mushroom" or "wand" or "rod" or "ring" && k.PackSlots == 0 && k.CarryPercent == 0).Take(40))
            if (inv.SlotsUsed < inv.PackSize) inv.Add(q.Game.Objects.Create(kind.Id));
        Assert.Equal(inv.PackSize, inv.SlotsUsed);
        Assert.True(inv.CanCarry(q.Game.Objects.Create(potions[3])));          // the belt has loops free
        foreach (var kind in potions.Skip(3).Take(5)) Assert.NotNull(inv.Add(q.Game.Objects.Create(kind)));
        Assert.Equal(8, inv.Belt.Count());
        Assert.Equal(inv.PackSize, inv.SlotsUsed);
        Assert.False(inv.CanCarry(q.Game.Objects.Create(potions[8])));         // a ninth kind needs a pack slot
        Assert.False(inv.CanCarry(q.Game.Objects.Create(KindsOf(q, "scroll", 1)[0]))); // (and the case itself needs one)
    }

    [Theory]
    [InlineData("cure_light_wounds", "cold")]
    [InlineData("phase_door", "fire")]
    public void The_belt_and_case_halve_the_chance_of_losing_what_they_hold(string kind, string element)
    {
        var q = Start();
        GiveHolders(q);
        var game = q.Game;
        var inv = game.Player.Inventory;
        foreach (var held in inv.Pack.Where(i => i.Kind.Id == kind).ToList())
            inv.Remove(held, held.Number, () => game.Objects.NextSerial++);
        var stack = inv.Add(game.Objects.Create(kind, 40))!;
        Assert.True(game.Sheltered(stack));
        Assert.False(game.Sheltered(game.Objects.Create(kind)));                // (only what's carried)

        game.InventoryDamage(element, 10_000);                                 // a sure hit, halved
        Assert.InRange(40 - stack.Number, 10, 30);                             // about twenty of forty lost, not all
    }
}
