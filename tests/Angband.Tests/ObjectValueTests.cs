using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2 object values (obj-power.c object_power, object_value_real, object_value). The
/// expected numbers are worked out by hand from Angband's formulas.
/// </summary>
public class ObjectValueTests
{
    private static readonly ObjectFactory Objects = new(TestData.Game);
    private static long Value(Item item) => ItemValue.Of(item, TestData.Game);

    [Fact]
    public void Weapons_ArePricedByPower_PowerTimesPowerPlusFive()
    {
        // Long sword 2d5: dice power 2 × 6 × 5 / 4 = 15, so 15 × 20 = 300.
        var sword = Objects.Create("long_sword");
        Assert.Equal(15, ObjectPower.Of(sword, TestData.Game));
        Assert.Equal(300, Value(sword));

        // (+5,+5): +12 for damage, +7 for to-hit: 34 × 39.
        sword.ToHit = sword.ToDam = 5;
        Assert.Equal(34, ObjectPower.Of(sword, TestData.Game));
        Assert.Equal(1326, Value(sword));

        // A dagger (1d4) is a throwing weapon too: 6 + 4.
        Assert.Equal(10 * 15, Value(Objects.Create("dagger")));
    }

    [Fact]
    public void Launchers_CountTheirAmmoAndMultiplier()
    {
        // Sling: shots (10 × 5 / 2 = 25) × 2, over 5 blows = 10.
        Assert.Equal(10 * 15, Value(Objects.Create("sling")));
        // Long bow: arrows (12 × 5 / 2 = 30) × 3 / 5 = 18.
        Assert.Equal(18 * 23, Value(Objects.Create("long_bow")));
    }

    [Fact]
    public void Ammunition_AndTorches_AreATwentiethOfTheirPowerPrice()
    {
        // Arrow 1d6: 8, scaled by the launcher (× 5 / 10) to 4: 4 × 9 / 20 = 1.
        Assert.Equal(1, Value(Objects.Create("arrow")));
        // Torch: light 2 × 3 power × 3 for a light = 18: 18 × 23 / 20 = 20.
        Assert.Equal(20, Value(Objects.Create("wooden_torch")));
    }

    [Fact]
    public void Armour_CountsItsArmourForItsWeight()
    {
        // Soft leather [8] at 8 lb: 1 + 8 × 75 / 100 = 7: 7 × 12.
        Assert.Equal(84, Value(Objects.Create("soft_leather_armour")));
        // A cloak's armour rounds away to nothing — but nothing is ever worth nothing.
        Assert.Equal(6, Value(Objects.Create("cloak")));
    }

    [Fact]
    public void Jewellery_CountsItsResistances_AndItsEffectsPower()
    {
        // Ring of Flames [+10]: 10 to AC + 4 jewellery + 6 fire resistance + 11 for its effect.
        var ring = Objects.Create("ring_of_flames");
        ring.ToAc = 10;
        Assert.Equal(31, ObjectPower.Of(ring, TestData.Game));
        Assert.Equal(31 * 36, Value(ring));
    }

    [Fact]
    public void Curses_TakeTheirObjectsPowerAway()
    {
        var sword = Objects.Create("long_sword");
        sword.Curses.Add("vulnerability"); // its object (4.2.5): -50 to AC and aggravation, worth -50 and -20
        Assert.Equal(15 - 50 - 20, ObjectPower.Of(sword, TestData.Game));
        Assert.Equal(0, Value(sword)); // negative power: worthless
    }

    [Fact]
    public void Devices_CostMoreForTheirCharges()
    {
        var wand = Objects.Create("wand_of_magic_missile");
        wand.Charges = 10;
        Assert.Equal(200 + 200 * 10 / 20, Value(wand));
        Assert.Equal(Objects.Create("cure_light_wounds").Kind.Cost, Value(Objects.Create("cure_light_wounds")));
    }

    [Fact]
    public void Artifacts_AddTheirActivationsPower()
    {
        var phial = Objects.CreateArtifact(TestData.Game.Artifacts.Single(a => a.Id == "galadriel"));
        Assert.True(phial.Artifact!.ActivationPower > 0);
        var withoutActivation = PowerProfile.Of(phial);
        var power = ObjectPower.Of(withoutActivation, TestData.Game);
        // 4.2.5's Phial: light 4 × 3 × 3 = 36, no fuel 5, ignoring the base elements 8, plus the activation.
        Assert.Equal(36 + 5 + 8 + phial.Artifact.ActivationPower, power);
    }

    [Fact]
    public void WhatYouKnow_IsWhatYouArePaidFor()
    {
        var game = Arena.Create(1);
        var sword = game.Objects.Create("long_sword");
        sword.ToHit = sword.ToDam = 5;
        var fresh = new PlayerKnowledge(TestData.Game, 1);
        Assert.Equal(300, ItemValue.Known(sword, TestData.Game, fresh)); // the bonuses aren't known
        fresh.LearnRune(RuneIds.ToHit);
        fresh.LearnRune(RuneIds.ToDam);
        Assert.Equal(1326, ItemValue.Known(sword, TestData.Game, fresh));

        // Unfamiliar flavours are guessed at by type.
        Assert.Equal(20, ItemValue.Known(game.Objects.Create("speed"), TestData.Game, new PlayerKnowledge(TestData.Game, 1)));
        // Jewellery is valued by what is known of it, flavour or not: 4 for a ring, 11 for its effect.
        Assert.Equal(15 * 20, ItemValue.Known(game.Objects.Create("ring_of_flames"), TestData.Game, new PlayerKnowledge(TestData.Game, 1)));
    }

    [Fact]
    public void Stores_PayForTheLesserOfTheTrueAndKnownValue()
    {
        var dir = Directory.CreateTempSubdirectory("avaband-value-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, Angband.Data.DataLoader.ConstantsFile), """{ "noSelling": false }""");
            var game = Angband.Core.Game.GameSession.NewGame(Angband.Data.DataLoader.Load(Angband.Data.DataLoader.DefaultDataDirectory, dir), 3);
            // Slay evil (power 200) adds 15 × 15 × 100 / 2500 = 9: worth 24 × 29, though you can't tell.
            var sword = game.Objects.Create("long_sword");
            sword.Slays.Add(TestData.Game.Egos.SelectMany(e => e.Slays).First(sl => sl.MonsterFlag == "EVIL" && sl.Multiplier == 2));
            var smith = game.Stores["weaponsmith"];
            Assert.False(game.Knowledge.KnowsRune(RuneIds.Slay("EVIL")));
            Assert.Equal(24 * 29, ItemValue.Of(sword, game.Data));
            Assert.Equal(300 * 2 / 3, game.SellPrice(smith, sword));
            Assert.Equal(24 * 29, game.BuyPrice(smith, sword)); // stores sell at the true value
        }
        finally { Directory.Delete(dir, true); }
    }
}
