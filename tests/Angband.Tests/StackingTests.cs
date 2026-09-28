using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2's stacking (obj-pile.c object_stackable, object_absorb_merge; obj-util.c
/// distribute_charges, number_charging, recharge_timeout).
/// </summary>
public class StackingTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(5);
        Arena.StripGear(game);
        foreach (var item in game.Player.Inventory.Pack.ToList()) game.Player.Inventory.Remove(item, item.Number, () => game.Objects.NextSerial++);
        return game;
    }

    private static Item Carry(GameSession game, string kind, Action<Item>? setup = null)
    {
        var item = game.Objects.Create(kind);
        game.Knowledge.LearnKind(item.Kind);
        setup?.Invoke(item);
        return game.Player.Inventory.Add(item)!;
    }

    [Fact]
    public void IdenticalArmour_Stacks_AndWieldingTakesOne()
    {
        var game = Game();
        var a = Carry(game, "soft_leather_armour", i => i.ToAc = 0);
        var b = Carry(game, "soft_leather_armour", i => i.ToAc = 0);
        Assert.Same(a, b);
        Assert.Equal(2, a.Number);
        Assert.StartsWith("2 Soft Leather Armours", game.Describe(a));

        game.Execute(new WieldCommand(a));
        Assert.Equal(1, a.Number);
        Assert.Equal(1, game.Player.Inventory.Equipped.Count(i => i.Kind.Id == "soft_leather_armour"));
    }

    [Fact]
    public void DifferentEnchantments_Modifiers_OrArtifacts_DontStack()
    {
        var game = Game();
        var plain = Carry(game, "soft_leather_armour", i => i.ToAc = 0);
        var better = Carry(game, "soft_leather_armour", i => i.ToAc = 3);
        Assert.NotSame(plain, better);

        var weak = Carry(game, "ring_of_strength", i => i.Modifiers["STR"] = 2);
        var strong = Carry(game, "ring_of_strength", i => i.Modifiers["STR"] = 3);
        Assert.NotSame(weak, strong);

        var art = game.Objects.CreateArtifact(game.Data.Artifacts.First(a => a.Kind == "dagger" || a.Id.Contains("dagger")));
        Assert.False(art.CanStackWith(art.Clone(game.Objects.NextSerial++, 1)));
    }

    [Fact]
    public void Wands_ShareTheirCharges_AndSplitThemFairly()
    {
        var game = Game();
        var wand = Carry(game, "wand_of_magic_missile", i => i.Charges = 3);
        Carry(game, "wand_of_magic_missile", i => i.Charges = 7);
        Assert.Equal(2, wand.Number);
        Assert.Equal(10, wand.Charges);
        Assert.Contains("(10 charges)", game.Describe(wand));

        var half = wand.Split(game.Objects.NextSerial++, 1);
        Assert.Equal(5, half.Charges);
        Assert.Equal(5, wand.Charges);
    }

    [Fact]
    public void Rods_InAStack_RechargeTogether()
    {
        var game = Game();
        var rod = Carry(game, "rod_of_treasure_location");
        Carry(game, "rod_of_treasure_location");
        game.Player.SkillDevice = 1000; // never fails to zap (this is about recharging)
        Assert.Equal(2, rod.Number);
        var time = rod.RechargeTime;
        Assert.True(time > 0);

        game.Execute(new UseCommand(rod));
        Assert.Equal(1, rod.NumberCharging);
        Assert.True(rod.RodReady); // the other one can still be zapped
        Assert.Contains("(1 charging)", game.Describe(rod));

        game.Execute(new UseCommand(rod));
        Assert.Equal(2, rod.NumberCharging);
        Assert.False(game.Execute(new UseCommand(rod)));

        // Both recharge at once: one step takes a turn off each.
        var timeout = rod.Timeout;
        rod.Recharge();
        Assert.Equal(timeout - 2, rod.Timeout);

        // Splitting one off gives it up to its own share of the recharge time.
        var one = rod.Split(game.Objects.NextSerial++, 1);
        Assert.Equal(timeout - 2, one.Timeout + rod.Timeout);
        Assert.True(one.Timeout <= time);
    }

    [Fact]
    public void RechargingActivations_AndChests_DontStack()
    {
        var game = Game();
        var a = game.Objects.Create("soft_leather_armour");
        var b = game.Objects.Create("soft_leather_armour");
        b.ToAc = a.ToAc;
        Assert.True(a.CanStackWith(b));
        b.Timeout = 5;
        Assert.False(a.CanStackWith(b));

        var chest = game.Objects.Create(game.Data.Objects.First(k => k.Base == "chest").Id);
        Assert.False(chest.CanStackWith(chest.Clone(game.Objects.NextSerial++, 1)));
    }

    [Fact]
    public void FloorPiles_MergeLikeThePack()
    {
        var game = Game();
        var spot = game.Player.Position + new Loc(1, 0);
        var first = game.Objects.Create("wand_of_magic_missile");
        first.Charges = 4;
        var second = game.Objects.Create("wand_of_magic_missile");
        second.Charges = 6;
        game.Level.Objects.Add(spot, first);
        game.Level.Objects.Add(spot, second);
        var pile = game.Level.Objects.At(spot);
        Assert.Single(pile);
        Assert.Equal((2, 10), (pile[0].Number, pile[0].Charges));
    }
}
