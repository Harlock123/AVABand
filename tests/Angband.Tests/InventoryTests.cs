using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Tests;

public class InventoryTests
{
    private readonly ObjectFactory _f = new(TestData.Game);
    private long Serial() => _f.NextSerial++;

    [Fact]
    public void Pack_HoldsAtMostItsSlotCount()
    {
        var inv = new Inventory(packSize: 3);
        Assert.NotNull(inv.Add(_f.Create("dagger")));
        Assert.NotNull(inv.Add(_f.Create("cloak")));
        Assert.NotNull(inv.Add(_f.Create("whip")));
        Assert.False(inv.CanCarry(_f.Create("spear")));
        Assert.Null(inv.Add(_f.Create("spear")));
    }

    [Fact]
    public void Stacks_Merge()
    {
        var inv = new Inventory();
        inv.Add(_f.Create("ration_of_food", 2));
        inv.Add(_f.Create("ration_of_food", 3));
        Assert.Single(inv.Pack);
        Assert.Equal(5, inv.Pack[0].Number);
    }

    [Fact]
    public void Missiles_GoToTheQuiver_AndUsePackSlotsPerForty()
    {
        var inv = new Inventory(packSize: 23, quiverSlotSize: 40);
        inv.Add(_f.Create("iron_shot", 40));
        Assert.Single(inv.Quiver);
        Assert.Empty(inv.Pack);
        Assert.Equal(1, inv.SlotsUsed);

        inv.Add(_f.Create("iron_shot", 1));
        Assert.Equal(2, inv.SlotsUsed);
    }

    [Fact]
    public void Wielding_SwapsWithTheCurrentItem()
    {
        var inv = new Inventory();
        var dagger = _f.Create("dagger");
        var sword = _f.Create("long_sword");
        inv.Add(dagger);
        inv.Add(sword);

        Assert.Null(inv.Wield(dagger, Serial));
        Assert.Equal(dagger, inv.Weapon);
        var previous = inv.Wield(sword, Serial);
        Assert.Equal(dagger, previous);
        Assert.Equal(sword, inv.Weapon);
    }

    [Fact]
    public void Rings_UseBothHands()
    {
        var inv = new Inventory();
        var a = _f.Create("ring_of_protection");
        var b = _f.Create("ring_of_protection");
        inv.Wield(a, Serial);
        inv.Wield(b, Serial);
        Assert.Equal(2, inv.Equipped.Count(i => i.Base.Slot == EquipSlot.Ring));
    }

    [Fact]
    public void Wielding_FromAStack_TakesOne()
    {
        var inv = new Inventory();
        var torches = _f.Create("wooden_torch", 3);
        inv.Add(torches);
        inv.Wield(torches, Serial);
        Assert.Equal(2, torches.Number);
        Assert.NotNull(inv.Light);
        Assert.Equal(1, inv.Light!.Number);
    }

    [Fact]
    public void Weight_CountsEverything()
    {
        var inv = new Inventory();
        inv.Add(_f.Create("iron_shot", 10));
        inv.Wield(_f.Create("soft_leather_armour"), Serial);
        Assert.Equal(10 * 5 + 80, inv.TotalWeight); // 4.2.5: a shot is half a pound
    }
}
